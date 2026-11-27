using UnityEngine;
using UnityEngine.UI;

namespace XCity
{
    // Two-finger camera control: the first gesture to pass its threshold (twist, pinch, vertical or
    // sideways drag) owns the touch until the fingers lift, so twisting no longer zooms and pinching
    // no longer rotates. Pan, rotation and zoom keep gliding briefly after release.
    public partial class XCityBootstrap
    {
        private enum TwoFingerMode
        {
            Undecided,
            Rotate,
            Zoom,
            Tilt,
            Pan
        }

        private const float RotateLockDegrees = 6f;
        private const float ZoomLockLogRatio = 0.095f; // about a 10% change in finger spread
        private const float MoveLockScreenFraction = 0.03f;
        private const float MinCamPitch = 25f;
        private const float MaxCamPitch = 80f;
        private const float TiltDegreesPerScreen = 70f;
        private const float InertiaDamping = 6f;

        private TwoFingerMode _twoFingerMode;
        private Vector2 _twoFingerStartSpan;
        private Vector2 _twoFingerStartMid;
        private Vector2 _panVelocity;
        private float _yawVelocity;
        private float _zoomVelocity;

        // Inertia glides v / InertiaDamping degrees, so this start speed turns the camera about 45°.
        private const float RotateStepSpeed = 45f * InertiaDamping;

        // Two buttons on the right edge, above the build bar: each tap glides the camera 45°.
        private void SetupRotateButtons()
        {
            var panel = CreatePanel(_safeAreaRoot, "Rotate", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-16f - 2 * UiTheme.MinTouch - 18f, BuildBarTop + 20f),
                new Vector2(-16f, BuildBarTop + 20f + UiTheme.MinTouch + 12f), UiTheme.Panel);
            UiTheme.Float(panel.GetComponent<Image>());
            var row = panel.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(6, 6, 6, 6);
            row.spacing = 6;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            CreateIconButton(panel.transform, UiIcon.RotateLeft, "RotateLeft").onClick.AddListener(() => RotateStep(-1f));
            CreateIconButton(panel.transform, UiIcon.RotateRight, "RotateRight").onClick.AddListener(() => RotateStep(1f));
        }

        private void RotateStep(float direction)
        {
            ClearFollow();
            _yawVelocity = direction * RotateStepSpeed;
        }

        private void BeginTwoFingerGesture(Touch first, Touch second)
        {
            _twoFingerMode = TwoFingerMode.Undecided;
            _twoFingerStartSpan = second.position - first.position;
            _twoFingerStartMid = (first.position + second.position) * 0.5f;
            StopCameraInertia();
        }

        private void ContinueTwoFingerGesture(Touch first, Touch second)
        {
            Vector2 span = second.position - first.position;
            Vector2 previousSpan = span - second.deltaPosition + first.deltaPosition;
            Vector2 mid = (first.position + second.position) * 0.5f;
            Vector2 midDelta = (first.deltaPosition + second.deltaPosition) * 0.5f;
            if (span.sqrMagnitude < 1f || previousSpan.sqrMagnitude < 1f) return;

            if (_twoFingerMode == TwoFingerMode.Undecided)
            {
                _twoFingerMode = PickTwoFingerMode(span, mid);
                if (_twoFingerMode == TwoFingerMode.Undecided) return;
            }

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            switch (_twoFingerMode)
            {
                case TwoFingerMode.Rotate:
                    float turn = Vector2.SignedAngle(previousSpan, span);
                    _camYaw -= turn;
                    _yawVelocity = Mathf.Lerp(_yawVelocity, turn / dt, 0.5f);
                    break;
                case TwoFingerMode.Zoom:
                    float zoom = Mathf.Log(previousSpan.magnitude / span.magnitude);
                    _camDistance = Mathf.Clamp(_camDistance * Mathf.Exp(zoom), MinCamDistance, MaxCamDistance);
                    _zoomVelocity = Mathf.Lerp(_zoomVelocity, zoom / dt, 0.5f);
                    break;
                case TwoFingerMode.Tilt:
                    // Swiping up lowers the camera toward the horizon, as in map apps.
                    _camPitch = Mathf.Clamp(_camPitch - midDelta.y * TiltDegreesPerScreen / Screen.height,
                        MinCamPitch, MaxCamPitch);
                    break;
                case TwoFingerMode.Pan:
                    ClearFollow();
                    PanCamera(midDelta);
                    _panVelocity = Vector2.Lerp(_panVelocity, midDelta / dt, 0.5f);
                    break;
            }
            UpdateCameraTransform();
        }

        private TwoFingerMode PickTwoFingerMode(Vector2 span, Vector2 mid)
        {
            if (Mathf.Abs(Vector2.SignedAngle(_twoFingerStartSpan, span)) > RotateLockDegrees)
                return TwoFingerMode.Rotate;
            if (Mathf.Abs(Mathf.Log(span.magnitude / _twoFingerStartSpan.magnitude)) > ZoomLockLogRatio)
                return TwoFingerMode.Zoom;
            Vector2 moved = mid - _twoFingerStartMid;
            if (moved.magnitude < MoveLockScreenFraction * Screen.height)
                return TwoFingerMode.Undecided;
            return Mathf.Abs(moved.y) > Mathf.Abs(moved.x) * 1.5f ? TwoFingerMode.Tilt : TwoFingerMode.Pan;
        }

        // Moves the camera so the ground follows the finger(s) by this many screen pixels.
        private void PanCamera(Vector2 screenDelta)
        {
            Vector3 right = Vector3.ProjectOnPlane(_cam.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
            float panSpeed = _camDistance * 1.6f / Screen.height;
            _camTarget -= (right * screenDelta.x + forward * screenDelta.y) * panSpeed;
            float maxBound = GridSize * CellSize;
            _camTarget.x = Mathf.Clamp(_camTarget.x, 0f, maxBound);
            _camTarget.z = Mathf.Clamp(_camTarget.z, 0f, maxBound);
        }

        private void TrackPanVelocity(Vector2 screenDelta)
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            _panVelocity = Vector2.Lerp(_panVelocity, screenDelta / dt, 0.5f);
        }

        private void StopCameraInertia()
        {
            _panVelocity = Vector2.zero;
            _yawVelocity = 0f;
            _zoomVelocity = 0f;
        }

        // Runs only while no finger or mouse button is down.
        private void UpdateCameraInertia()
        {
            if (Input.touchCount > 0 || _isDragging) return;
            float dt = Time.unscaledDeltaTime;
            if (_panVelocity.sqrMagnitude < 25f && Mathf.Abs(_yawVelocity) < 2f && Mathf.Abs(_zoomVelocity) < 0.02f)
            {
                StopCameraInertia();
                return;
            }

            if (_panVelocity.sqrMagnitude >= 25f)
            {
                ClearFollow();
                PanCamera(_panVelocity * dt);
            }
            _camYaw -= _yawVelocity * dt;
            _camDistance = Mathf.Clamp(_camDistance * Mathf.Exp(_zoomVelocity * dt), MinCamDistance, MaxCamDistance);
            UpdateCameraTransform();

            float decay = Mathf.Exp(-InertiaDamping * dt);
            _panVelocity *= decay;
            _yawVelocity *= decay;
            _zoomVelocity *= decay;
        }
    }
}
