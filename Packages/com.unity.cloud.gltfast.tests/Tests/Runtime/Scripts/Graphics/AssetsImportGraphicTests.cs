// SPDX-FileCopyrightText: 2025 Unity Technologies and the glTFast authors
// SPDX-License-Identifier: Apache-2.0

#if USING_GRAPHICS_TEST_FRAMEWORK
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Schema;
using GLTFast.Logging;
using GLTFast.Tests;
using GLTFast.Tests.Import;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools.Graphics;

namespace GLTFast.Tests.Graphics
{
    [Category("Graphics")]
    class ImportGraphicsTests
    {
        enum ViewType
        {
            Top,
            Bottom,
            Left,
            Right,
            Front,
            Back,
            Perspective
        }
        private const string DefaultReferenceImageName = "DefaultReferenceImage.png";
        public Bounds Bounds { get; private set; }

        [GltfTestCase("glTF-Graphic-Tests-Assets", 2, testPrefix: "gfx-Top-")]
        public IEnumerator TopView(GltfTestCaseSet testCaseSet, GltfTestCase testCase)
        {
            yield return AsyncWrapper.WaitForTask(RunTestCaseAsync(testCaseSet, testCase, ViewType.Top));
        }

        [GltfTestCase("glTF-Graphic-Tests-Assets", 2, testPrefix: "gfx-Bottom-")]
        public IEnumerator BottomView(GltfTestCaseSet testCaseSet, GltfTestCase testCase)
        {
            yield return AsyncWrapper.WaitForTask(RunTestCaseAsync(testCaseSet, testCase, ViewType.Bottom));
        }

        [GltfTestCase("glTF-Graphic-Tests-Assets", 2, testPrefix: "gfx-Left-")]
        public IEnumerator LeftView(GltfTestCaseSet testCaseSet, GltfTestCase testCase)
        {
            yield return AsyncWrapper.WaitForTask(RunTestCaseAsync(testCaseSet, testCase, ViewType.Left));
        }

        [GltfTestCase("glTF-Graphic-Tests-Assets", 2, testPrefix: "gfx-Right-")]

        public IEnumerator RightView(GltfTestCaseSet testCaseSet, GltfTestCase testCase)
        {
            yield return AsyncWrapper.WaitForTask(RunTestCaseAsync(testCaseSet, testCase, ViewType.Right));
        }

        [GltfTestCase("glTF-Graphic-Tests-Assets", 2, testPrefix: "gfx-Front-")]
        public IEnumerator FrontView(GltfTestCaseSet testCaseSet, GltfTestCase testCase)
        {
            yield return AsyncWrapper.WaitForTask(RunTestCaseAsync(testCaseSet, testCase, ViewType.Front));
        }
        [GltfTestCase("glTF-Graphic-Tests-Assets", 2, testPrefix: "gfx-Back-")]
        public IEnumerator BackView(GltfTestCaseSet testCaseSet, GltfTestCase testCase)
        {
            yield return AsyncWrapper.WaitForTask(RunTestCaseAsync(testCaseSet, testCase, ViewType.Back));
        }

        [GltfTestCase("glTF-Graphic-Tests-Assets", 2, testPrefix: "gfx-Perspective-")]
        public IEnumerator PerspectiveView(GltfTestCaseSet testCaseSet, GltfTestCase testCase)
        {
            yield return AsyncWrapper.WaitForTask(RunTestCaseAsync(testCaseSet, testCase, ViewType.Perspective));
        }

        private async Task RunTestCaseAsync(GltfTestCaseSet testCaseSet, GltfTestCase testCase, ViewType view)
        {
#if UNITY_ENTITIES_GRAPHICS
            Assert.Ignore("Graphics tests are not implemented with Entities.");
#endif
            // Create GameObject root for imported glTF scene
            var rootGameObject = new GameObject("GLTF Object");
            var deferAgent = new UninterruptedDeferAgent();
            var loadLogger = new CollectingLogger();

            // Build the file path
            var filePath = Path.Combine(testCaseSet.RootPath, testCase.relativeUri);
            using var gltfImport = new GltfImport(deferAgent: deferAgent, logger: loadLogger);
            var loadSuccess = await gltfImport.Load(filePath);

            // Validate loading process
            if (loadSuccess == testCase.expectLoadFail)
            {
                throw new AssertionException(loadSuccess
                    ? "glTF import unexpectedly succeeded while failure was expected."
                    : "glTF import failed unexpectedly.");
            }

            // Setup the main instantiator
            var instantiatorLogger = new CollectingLogger();
            var instantiator = CreateInstantiator(gltfImport, instantiatorLogger, rootGameObject.transform);

            // Instantiate the glTF model into the scene
            var instantiateSuccess = await gltfImport.InstantiateMainSceneAsync(instantiator);
            if (!instantiateSuccess)
            {
                instantiatorLogger.LogAll();
                throw new AssertionException("glTF instantiation failed.");
            }
            // Calculate and set the bounds for the instantiated object
            SetBounds(instantiator);

            // Add and configure a main camera
            var cameraGameObject = CreateAndConfigureCamera(rootGameObject, view);
            ImageAssert.AreEqual(
                LoadReferenceImage("gfx-"+ view +"-"+ testCase),
                cameraGameObject.GetComponent<Camera>(), settings: new ImageComparisonSettings()
                {
                    AverageCorrectnessThreshold = 0.0015f,
                    PerPixelCorrectnessThreshold = 0.00015f
                });
        }

        void SetBounds(GameObjectBoundsInstantiator instantiator)
        {
            var sceneBounds = instantiator.SceneInstance != null ? instantiator.CalculateBounds() : null;
            if (sceneBounds.HasValue)
            {
                Bounds = sceneBounds.Value;
            }
        }

        /// <summary>
        /// Creates and configures a camera for rendering the imported object from different views.
        /// </summary>
        private GameObject CreateAndConfigureCamera(GameObject rootGameObject, ViewType view)
        {

            Transform objectTransform = rootGameObject.transform;
            var cameraGameObject = new GameObject("Main Camera");
            cameraGameObject.transform.SetParent(objectTransform);
            var camera = cameraGameObject.AddComponent<Camera>();
            float fieldOfView = 60;
            camera.fieldOfView = fieldOfView;
            float3 scale = objectTransform.localScale;
            float3 boundsSize = Bounds.size;

            var distance = math.length(scale * boundsSize);

            var angle = math.radians(20);

            var centerPosition = objectTransform.TransformPoint(Bounds.center);
            var cameraPos = centerPosition +
                            new Vector3(0, distance * math.sin(angle), distance * math.cos(angle));
            camera.transform.position = cameraPos;
            camera.transform.LookAt(centerPosition);

            camera.nearClipPlane = distance * .001f;
            camera.farClipPlane = distance * 3;
            switch (view)
            {
                case ViewType.Top:
                    camera.transform.position = centerPosition + new Vector3(0, distance, 0);
                    break;
                case ViewType.Bottom:
                    camera.transform.position = centerPosition + new Vector3(0, -distance, 0);
                    break;
                case ViewType.Left:
                    camera.transform.position = centerPosition + new Vector3(-distance, 0, 0);
                    break;
                case ViewType.Right:
                    camera.transform.position = centerPosition + new Vector3(distance, 0, 0);
                    break;
                case ViewType.Front:
                    camera.transform.position = centerPosition + new Vector3(0, 0, distance);
                    break;
                case ViewType.Back:
                    camera.transform.position = centerPosition + new Vector3(0, 0, -distance);
                    break;
                case ViewType.Perspective:
                    camera.transform.position = centerPosition + new Vector3(distance, 0, distance);
                    break;
            }

            camera.transform.LookAt(centerPosition);
            FrameObject(Bounds, camera);
            return cameraGameObject;
        }

        /// <summary>
        /// Loads a reference image to compare the rendered output.
        /// </summary>
        private static Texture2D LoadReferenceImage(string filename)
        {
            const string baseReferencePath = "Assets/ReferenceImages/";
            var referenceImage = new Texture2D(1, 1);
            var referenceImagePath = Path.Combine(baseReferencePath, $"{filename}.png");
            var imageBytes = File.Exists(referenceImagePath)
                ? File.ReadAllBytes(referenceImagePath)
                : File.ReadAllBytes(Path.Combine(baseReferencePath, DefaultReferenceImageName));
            referenceImage.LoadImage(imageBytes);
            return referenceImage;
        }
        void FrameObject(Bounds bounds, Camera camera)
            {
                Vector3 boxCenter = bounds.center;
                Vector3 boxExtents = bounds.extents;

                float aspect = (float)Screen.width / Screen.height;
                float fov = camera.fieldOfView;

                float boxHeight = boxExtents.y * 2;
                float boxWidth = boxExtents.x * 2;
                float maxDimension = math.length(Bounds.size);

                float fovRad = fov * Mathf.Deg2Rad;
                float Distance = (maxDimension / 2) / Mathf.Tan(fovRad / 2);

                Vector3 cameraPosition = boxCenter - camera.transform.forward * Distance;
                camera.transform.position = cameraPosition;
                camera.transform.LookAt(boxCenter);
            }

        /// <summary>
        /// Creates an appropriate instantiator based on the runtime environment.
        /// </summary>
        internal static GameObjectBoundsInstantiator CreateInstantiator(
            IGltfReadable gltf,
            ICodeLogger logger,
            Transform parentTransform)
        {
            return new GameObjectBoundsInstantiator(gltf, parentTransform, logger);
        }
    }
}
#endif // USING_GRAPHICS_TEST_FRAMEWORK
