using UnityEngine;

namespace Bosque.ForestRendering
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class ForestPresentationCamera : MonoBehaviour
    {
        public Transform pivot;
        public float orbitRadius = 92f;
        public float orbitHeight = 28f;
        public float lookAtHeight = 8f;
        public float orbitDegreesPerSecond = 7f;
        public float breathingZoom = 7f;
        public bool automaticPullback = true;
        public float closeRadius = 30f;
        public float closeHeight = 12f;
        public float wideRadius = 92f;
        public float wideHeight = 28f;
        public float closeSeconds = 4.5f;
        public float pullbackSeconds = 15f;

        Camera cachedCamera;
        float startTime;

        void Awake()
        {
            cachedCamera = GetComponent<Camera>();
            cachedCamera.fieldOfView = 48f;
        }

        void OnEnable()
        {
            startTime = Time.time;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
                return;

            Vector3 center = pivot != null ? pivot.position : Vector3.zero;
            float angle = Time.time * orbitDegreesPerSecond * Mathf.Deg2Rad;
            float radius = orbitRadius;
            float height = orbitHeight;

            if (automaticPullback)
            {
                float t = Mathf.Clamp01((Time.time - startTime - closeSeconds) / Mathf.Max(0.1f, pullbackSeconds));
                t = t * t * (3f - 2f * t);
                radius = Mathf.Lerp(closeRadius, wideRadius, t);
                height = Mathf.Lerp(closeHeight, wideHeight, t);
            }

            radius += Mathf.Sin(Time.time * 0.23f) * breathingZoom;

            Vector3 position = center + new Vector3(
                Mathf.Cos(angle) * radius,
                height + Mathf.Sin(Time.time * 0.17f) * 3f,
                Mathf.Sin(angle) * radius);

            transform.position = position;
            transform.LookAt(center + Vector3.up * lookAtHeight);
        }
    }
}
