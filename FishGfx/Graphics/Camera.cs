using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using OpenGL;
using System.Numerics;

//using Matrix4 = System.Numerics.Matrix4x4;

namespace FishGfx.Graphics {
	public class Camera {
		public float Near { get; private set; }
		public float Far { get; private set; }
		public float VerticalFOV { get; private set; }
		public float HorizontalFOV { get; private set; }

		Matrix4x4 _View;
		Matrix4x4 _World;
		Vector3 _Position;
		Quaternion _Rotation;

		public Matrix4x4 View { get { Refresh(); return _View; } private set { _View = value; } }
		public Matrix4x4 World { get { Refresh(); return _World; } private set { _World = value; } }
		public Matrix4x4 Projection { get; private set; }
		public Vector3 Position { get { return _Position; } set { Dirty = true; _Position = value; } }
		public Quaternion Rotation { get { return _Rotation; } set { Dirty = true; _Rotation = value; } }

		bool Dirty;
		public Vector2 ViewportSize { get; private set; }

		public Vector3 ForwardNormal { get { return -Vector3.UnitZ; } }
		public Vector3 RightNormal { get { return Vector3.UnitX; } }
		public Vector3 UpNormal { get { return Vector3.UnitY; } }

		public Vector3 WorldForwardNormal { get; private set; }
		public Vector3 WorldRightNormal { get; private set; }
		public Vector3 WorldUpNormal { get; private set; }

		public bool MouseMovement;
		float Yaw, Pitch;
		public Vector3 CameraUpNormal;

		public Vector2 PitchClamp = new Vector2(-90, 90);

		public Camera() {
			View = Matrix4x4.Identity;
			Projection = Matrix4x4.Identity;
			Position = new Vector3(0, 0, 0);
			Rotation = Quaternion.CreateFromYawPitchRoll(0, 0, 0);
			MouseMovement = false;
			CameraUpNormal = UpNormal;
		}

		public void SetOrthogonal(float Left, float Bottom, float Right, float Top, float NearPlane = 1, float FarPlane = 10000, bool PreserveCenter = false) {
			Projection = Matrix4x4.CreateOrthographicOffCenter(Left, Right, Bottom, Top, NearPlane, FarPlane);

			float Width = Math.Abs(Left - Right);
			float Height = Math.Abs(Bottom - Top);
			ViewportSize = new Vector2(Width, Height);

			this.Near = NearPlane;
			this.Far = FarPlane;
		}

		public void SetPerspective(float Width, float Height, float HFOV = 1.5708f, float NearPlane = 1, float FarPlane = 7500, bool PreserveCenter = false) {
			HorizontalFOV = HFOV;
			Projection = Matrix4x4.CreatePerspectiveFieldOfView(VerticalFOV = VerticalFOVFromHorizontal(HFOV, Width, Height), Width / Height, NearPlane, FarPlane);
			ViewportSize = new Vector2(Width, Height);

			this.Near = NearPlane;
			this.Far = FarPlane;
		}

		public void SetPerspective(Vector2 Viewport, float HFOV = 1.5708f, float NearPlane = 1, float FarPlane = 7500, bool PreserveCenter = false) {
			SetPerspective(Viewport.X, Viewport.Y, HFOV, NearPlane, FarPlane, PreserveCenter);
		}

		public void LookAt(Vector3 Pos) {
			Matrix4x4 ViewLookAt = Matrix4x4.CreateLookAt(Position, Pos, UpNormal);
			Matrix4x4.Invert(ViewLookAt, out Matrix4x4 WorldLookAt);

			Matrix4x4.Decompose(WorldLookAt, out Vector3 WorldScale, out Quaternion WorldRotation, out Vector3 WorldTranslation);
			Position = WorldTranslation;
			Rotation = WorldRotation;

			(Pitch, Yaw, _) = Rotation;
			PerformClamps();
		}

		void Refresh() {
			if (!Dirty)
				return;
			Dirty = false;

			World = CreateModel(Position, Vector3.One, Rotation);
			WorldForwardNormal = Vector3.Normalize(Vector4.Transform(new Vector4(ForwardNormal, 0), World).XYZ());
			WorldRightNormal = Vector3.Normalize(Vector4.Transform(new Vector4(RightNormal, 0), World).XYZ());
			WorldUpNormal = Vector3.Normalize(Vector4.Transform(new Vector4(UpNormal, 0), World).XYZ());

			Matrix4x4.Invert(World, out Matrix4x4 ViewMat);
			View = ViewMat;
		}

		void PerformClamps() {
			Pitch = Pitch.Clamp(PitchClamp.X, PitchClamp.Y);
			Rotation = Quaternion.CreateFromYawPitchRoll(Yaw * (float)Math.PI / 180, Pitch * (float)Math.PI / 180, 0);
		}

		public void Update(Vector2 MouseDelta) {
			const float MouseScale = 1.0f / 5f;
			const float MaxAngle = 360;

			if (MouseMovement && (MouseDelta.X != 0 || MouseDelta.Y != 0)) {
				Yaw -= MouseDelta.X * MouseScale;
				while (Yaw > MaxAngle)
					Yaw -= MaxAngle;
				while (Yaw < 0)
					Yaw += MaxAngle;

				Pitch -= MouseDelta.Y * MouseScale;
				PerformClamps();
			}
		}

		public static Camera Create(Func<Camera> C) {
			return C();
		}

		public static float VerticalFOVFromHorizontal(float FOV, float Width, float Height) {
			return 2 * (float)Math.Atan(Math.Tan(FOV / 2) * (Height / Width));
		}

		public static Matrix4x4 CreateModel(Vector3 Position, Vector3 Scale, Quaternion Rotation) {
			return Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);
		}
	}
}