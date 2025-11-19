// SPDX-FileCopyrightText: 2023 Unity Technologies and the glTFast authors
// SPDX-License-Identifier: Apache-2.0

#if UNITY_ENTITIES_GRAPHICS

using System;
using System.Collections.Generic;

using GLTFast.Logging;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Profiling;
#if UNITY_ENTITIES_GRAPHICS
using Unity.Entities.Graphics;
using UnityEngine.Rendering;
#endif

namespace GLTFast {
    public class EntityInstantiator : IInstantiator {

        const float k_Epsilon = .00001f;

        protected ICodeLogger m_Logger;

        protected IGltfReadable m_Gltf;

        protected Entity m_Parent;

        protected Dictionary<uint,Entity> m_Nodes;

        protected InstantiationSettings m_Settings;

        EntityManager m_EntityManager;
        EntityArchetype m_NodeArchetype;
        EntityArchetype m_SceneArchetype;

        Parent m_SceneParent;

        public EntityInstantiator(
            IGltfReadable gltf,
            Entity parent,
            ICodeLogger logger = null,
            InstantiationSettings settings = null
            )
        {
            m_Gltf = gltf;
            m_Parent = parent;
            m_Logger = logger;
            m_Settings = settings ?? new InstantiationSettings();
        }

        /// <inheritdoc />
        public void BeginScene(
            string name,
            uint[] nodeIndices
        ) {
            Profiler.BeginSample("BeginScene");
            m_Nodes = new Dictionary<uint, Entity>();
            m_EntityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            m_NodeArchetype = m_EntityManager.CreateArchetype(
                typeof(Disabled),
                typeof(LocalTransform),
                typeof(Parent),
                typeof(LocalToWorld)
            );
            m_SceneArchetype = m_EntityManager.CreateArchetype(
                typeof(Disabled),
                typeof(LocalTransform)
            );

            if (m_Settings.SceneObjectCreation == SceneObjectCreation.Never
                || m_Settings.SceneObjectCreation == SceneObjectCreation.WhenMultipleRootNodes && nodeIndices.Length == 1) {
                m_SceneParent.Value = m_Parent;
            }
            else {
                var sceneEntity = m_EntityManager.CreateEntity(m_Parent==Entity.Null ? m_SceneArchetype : m_NodeArchetype);
                m_EntityManager.SetComponentData(sceneEntity,LocalTransform.Identity);
                m_EntityManager.SetComponentData(sceneEntity, new LocalToWorld{Value = float4x4.identity});
#if UNITY_EDITOR
                m_EntityManager.SetName(sceneEntity, name ?? "Scene");
#endif
                if (m_Parent != Entity.Null) {
                    m_EntityManager.SetComponentData(sceneEntity, new Parent { Value = m_Parent });
                }
                m_SceneParent.Value = sceneEntity;
            }
            Profiler.EndSample();
        }

#if UNITY_ANIMATION
        /// <inheritdoc />
        public void AddAnimation(AnimationClip[] animationClips) {
            if ((m_Settings.Mask & ComponentType.Animation) != 0 && animationClips != null) {
                // TODO: Add animation support
            }
        }
#endif // UNITY_ANIMATION

        /// <inheritdoc />
        public void CreateNode(
            uint nodeIndex,
            uint? parentIndex,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale
        ) {
            Profiler.BeginSample("CreateNode");
            var node = m_EntityManager.CreateEntity(m_NodeArchetype);
            var isUniformScale = IsUniform(scale);
            m_EntityManager.SetComponentData(
                node,
                new LocalTransform
                {
                    Position = position,
                    Rotation = rotation,
                    Scale = isUniformScale ? scale.x : 1f
                });
            if (!isUniformScale)
            {
                // TODO: Maybe instantiating another archetype instead of adding components here is more performant?
                m_EntityManager.AddComponent<PostTransformMatrix>(node);
                m_EntityManager.SetComponentData(
                    node,
                    new PostTransformMatrix { Value = float4x4.Scale(scale) }
                    );
            }
            m_Nodes[nodeIndex] = node;
            m_EntityManager.SetComponentData(
                node,
                parentIndex.HasValue
                    ? new Parent { Value = m_Nodes[parentIndex.Value] }
                    : m_SceneParent
                );
            Profiler.EndSample();
        }

        public void SetNodeName(uint nodeIndex, string name) {
#if UNITY_EDITOR
            m_EntityManager.SetName(m_Nodes[nodeIndex], name ?? $"Node-{nodeIndex}");
#endif
        }

        /// <inheritdoc />
        public virtual void AddPrimitive(
            uint nodeIndex,
            string meshName,
            MeshResult meshResult,
            uint[] joints = null,
            uint? rootJoint = null,
            float[] morphTargetWeights = null,
            int meshNumeration = 0
        ) {
            if ((m_Settings.Mask & ComponentType.Mesh) == 0) {
                return;
            }
            Profiler.BeginSample("AddPrimitive");
            Entity node;
            if(meshNumeration==0) {
                // Use Node GameObject for first Primitive
                node = m_Nodes[nodeIndex];
            } else {
                node = m_EntityManager.CreateEntity(m_NodeArchetype);
                m_EntityManager.SetComponentData(node,LocalTransform.Identity);
                m_EntityManager.SetComponentData(node, new Parent { Value = m_Nodes[nodeIndex] });
            }

            var materials = new Material[meshResult.materialIndices.Length];
            for (var index = 0; index < meshResult.materialIndices.Length; index++)
            {
                materials[index] = m_Gltf.GetMaterial(meshResult.materialIndices[index]) ?? m_Gltf.GetDefaultMaterial();
            }

            var filterSettings = RenderFilterSettings.Default;
            filterSettings.ShadowCastingMode = ShadowCastingMode.Off;
            filterSettings.ReceiveShadows = false;
            filterSettings.Layer = m_Settings.Layer;

            var renderMeshDescription = new RenderMeshDescription
            {
                FilterSettings = filterSettings,
                LightProbeUsage = LightProbeUsage.Off,
            };

            var renderMeshArray = new RenderMeshArray(materials, new[] { meshResult.mesh });

            for (ushort index = 0; index < meshResult.materialIndices.Length; index++)
            {
                RenderMeshUtility.AddComponents(
                    node,
                    m_EntityManager,
                    renderMeshDescription,
                    renderMeshArray,
                    MaterialMeshInfo.FromRenderMeshArrayIndices(
                        index,
                        0,
#if UNITY_ENTITIES_1_2_OR_NEWER
                        index
#else
                        (sbyte)index
#endif
                        )
                    );

                m_EntityManager.SetComponentData(node, new RenderBounds {Value = meshResult.mesh.bounds.ToAABB()} );
            }

            Profiler.EndSample();
        }

        /// <inheritdoc />
        public void AddPrimitiveInstanced(
            uint nodeIndex,
            string meshName,
            MeshResult meshResult,
            uint instanceCount,
            NativeArray<Vector3>? positions,
            NativeArray<Quaternion>? rotations,
            NativeArray<Vector3>? scales,
            int meshNumeration = 0
        ) {
            if ((m_Settings.Mask & ComponentType.Mesh) == 0) {
                return;
            }
            Profiler.BeginSample("AddPrimitiveInstanced");
            var materials = new Material[meshResult.materialIndices.Length];
            for (var index = 0; index < meshResult.materialIndices.Length; index++)
            {
                materials[index] = m_Gltf.GetMaterial(meshResult.materialIndices[index]) ?? m_Gltf.GetDefaultMaterial();
                materials[index].enableInstancing = true;
            }

            var filterSettings = RenderFilterSettings.Default;
            filterSettings.ShadowCastingMode = ShadowCastingMode.Off;
            filterSettings.ReceiveShadows = false;
            filterSettings.Layer = m_Settings.Layer;

            var renderMeshDescription = new RenderMeshDescription
            {
                FilterSettings = filterSettings,
                LightProbeUsage = LightProbeUsage.Off,
            };

            var renderMeshArray = new RenderMeshArray(materials, new[] { meshResult.mesh });
            for (ushort index = 0; index < meshResult.materialIndices.Length; index++)
            {
                var prototype = m_EntityManager.CreateEntity(m_NodeArchetype);
                m_EntityManager.SetEnabled(prototype, true);

                for (var i = 0; i < instanceCount; i++) {
                    var instance = i>0 ? m_EntityManager.Instantiate(prototype) : prototype;

                    var transform = new LocalTransform
                    {
                        Position = positions?[i] ?? Vector3.zero,
                        Rotation = rotations?[i] ?? Quaternion.identity,
                        Scale = 1
                    };
                    if (scales.HasValue)
                    {
                        var scale = scales.Value[i];
                        var isUniformScale = IsUniform(scale);
                        if (!isUniformScale)
                        {
                            m_EntityManager.AddComponent<PostTransformMatrix>(instance);
                            m_EntityManager.SetComponentData(instance,new PostTransformMatrix {Value = float4x4.Scale(scale)});
                        }
                        else
                        {
                            transform.Scale = scale.x;
                        }
                    }

                    m_EntityManager.SetComponentData(instance,transform);
                    m_EntityManager.SetComponentData(instance, new Parent { Value = m_Nodes[nodeIndex] });

                    RenderMeshUtility.AddComponents(
                        instance,
                        m_EntityManager,
                        renderMeshDescription,
                        renderMeshArray,
                        MaterialMeshInfo.FromRenderMeshArrayIndices(
                            index,
                            0,
#if UNITY_ENTITIES_1_2_OR_NEWER
                            index
#else
                            (sbyte)index
#endif
                        )
                    );
                }

            }
            Profiler.EndSample();
        }

        /// <inheritdoc />
        public void AddCamera(uint nodeIndex, uint cameraIndex) {
            // if ((m_Settings.mask & ComponentType.Camera) == 0) {
            //     return;
            // }
            // var camera = m_Gltf.GetSourceCamera(cameraIndex);
            // TODO: Add camera support
        }

        /// <inheritdoc />
        public void AddLightPunctual(
            uint nodeIndex,
            uint lightIndex
        ) {
            // if ((m_Settings.mask & ComponentType.Light) == 0) {
            //     return;
            // }
            // TODO: Add lights support
        }

        /// <inheritdoc />
        public virtual void EndScene(uint[] rootNodeIndices) {
            Profiler.BeginSample("EndScene");
            m_EntityManager.SetEnabled(m_SceneParent.Value, true);
            foreach (var entity in m_Nodes.Values) {
                m_EntityManager.SetEnabled(entity, true);
            }
            Profiler.EndSample();
        }

        static bool IsUniform(Vector3 scale)
        {
            return Math.Abs(scale.x - scale.y) < k_Epsilon && Math.Abs(scale.x - scale.z) < k_Epsilon;
        }
    }
}

#endif // UNITY_ENTITIES_GRAPHICS
