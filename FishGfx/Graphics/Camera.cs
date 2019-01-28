using System;
using System.Collections.Generic;
using System.Linq;
//using OpenGL;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

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

		Vector3 _WorldForwardNormal, _WorldRightNormal, _WorldUpNormal;
		public Vector3 WorldForwardNormal { get { Refresh(); return _WorldForwardNormal; } private set { _WorldForwardNormal = value; } }
		public Vector3 WorldRightNormal { get { Refresh(); return _WorldRightNormal; } private set { _WorldRightNormal = value; } }
		public Vector3 WorldUpNormal { get { Refresh(); return _WorldUpNormal; } private set { _WorldUpNormal = value; } }

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

		public void LookAtFitToScreen(Vector3 Target, float Radius) {
			Vector3 Eye = Position;

			Vector3 ToEye = Vector3.Normalize(Eye - Target);

			float Tan = (float)Math.Tan(Math.Min(HorizontalFOV, VerticalFOV) * 0.5f);
			float Distance = Radius / Tan;

			Position = Target + (Distance * ToEye);
			LookAt(Target);
		}

		public void SetOrthogonal(float Left, float Bottom, float Right, float Top, float NearPlane = 1, float FarPlane = 10000/*, bool PreserveCenter = false*/) {
			Projection = Matrix4x4.CreateOrthographicOffCenter(Left, Right, Bottom, Top, NearPlane, FarPlane);

			float Width = Math.Abs(Left - Right);
			float Height = Math.Abs(Bottom - Top);
			ViewportSize = new Vector2(Width, Height);

			this.Near = NearPlane;
			this.Far = FarPlane;
		}

		public void SetPerspective(float Width, float Height, float HFOV = 1.5708f, float NearPlane = 1, float FarPlane = 7500/*, bool PreserveCenter = false*/) {
			HorizontalFOV = HFOV;
			Projection = Matrix4x4.CreatePerspectiveFieldOfView(VerticalFOV = VerticalFOVFromHorizontal(HFOV, Width, Height), Width / Height, NearPlane, FarPlane);
			ViewportSize = new Vector2(Width, Height);

			this.Near = NearPlane;
			this.Far = FarPlane;
		}

		public void SetPerspective(Vector2 Viewport, float HFOV = 1.5708f, float NearPlane = 1, float FarPlane = 7500/*, bool PreserveCenter = false*/) {
			SetPerspective(Viewport.X, Viewport.Y, HFOV, NearPlane, FarPlane/*, PreserveCenter*/);
		}

		public void SetPerspectiveOffCenter(float Left, float Bottom, float Right, float Top, float NearPlane = 1, float FarPlane = 10000) {
			Projection = Matrix4x4.CreatePerspectiveOffCenter(Left, Right, Bottom, Top, NearPlane, FarPlane);

			float Width = Math.Abs(Left - Right);
			float Height = Math.Abs(Bottom - Top);
			ViewportSize = new Vector2(Width, Height);

			this.Near = NearPlane;
			this.Far = FarPlane;
		}

		public void LookAt(Vector3 Pos) {
			Matrix4x4 ViewLookAt = Matrix4x4.CreateLookAt(Position, Pos, UpNormal);
			Matrix4x4.Invert(ViewLookAt, out Matrix4x4 WorldLookAt);

			Matrix4x4.Decompose(WorldLookAt, out Vector3 WorldScale, out Quaternion WorldRotation, out Vector3 WorldTranslation);
			Position = WorldTranslation;
			Rotation = WorldRotation;

			//(Pitch, Yaw, _) = Rotation;
			Rotation.Deconstruct(out Pitch, out Yaw, out float _);

			PerformClamps();
		}

		public Vector3 WorldToScreen(Vector3 World, Matrix4x4 ModelMatrix) {
			Vector4 Pt = Vector4.Transform(new Vector4(World, 1.0f), Projection * View * ModelMatrix);
			return Pt.XYZ() / Pt.W;
		}

		public Vector3 WorldToScreen(Vector3 World) {
			return WorldToScreen(World, Matrix4x4.Identity);
		}

		public Vector3 ScreenToWorld(Vector2 Screen, Matrix4x4 ModelMatrix) {
			Matrix4x4 ProjView = Projection * View * ModelMatrix;
			Matrix4x4.Invert(ProjView, out Matrix4x4 InvProjView);

			Vector4 Pt = new Vector4((Screen / (ViewportSize / 2)) - Vector2.One, 0, 1);
			Pt = Vector4.Transform(Pt, InvProjView);
			Pt /= Pt.W;
			Pt.Z = 0;

			return Pt.XYZ();
		}

		public Vector3 ScreenToWorldDirection(Vector2 Screen, Matrix4x4 ModelMatrix) {
			return Vector3.Normalize(ScreenToWorld(Screen, ModelMatrix) - Position);
		}

		public Vector3 ScreenToWorldDirection(Vector2 Screen) {
			return ScreenToWorldDirection(Screen, Matrix4x4.Identity);
		}

		void Refresh() {
			if (!Dirty)
				return;

			Dirty = false;

			World = CreateModel(Position, Vector3.One, Rotation);
			WorldForwardNormal = ToWorldNormal(ForwardNormal);
			WorldRightNormal = ToWorldNormal(RightNormal);
			WorldUpNormal = ToWorldNormal(UpNormal);

			Matrix4x4.Invert(World, out Matrix4x4 ViewMat);
			View = ViewMat;
		}

		void PerformClamps() {
			Pitch = Pitch.Clamp(PitchClamp.X, PitchClamp.Y);
			Rotation = Quaternion.CreateFromYawPitchRoll(Yaw * (float)Math.PI / 180, Pitch * (float)Math.PI / 180, 0);
		}

		public Vector3 ToWorldNormal(Vector3 V) {
			Vector3 Ret = Vector4.Transform(new Vector4(V, 0), World).XYZ();

			if (Ret.X == 0 && Ret.Y == 0 && Ret.Z == 0)
				return Vector3.Zero;

			return Vector3.Normalize(Ret);
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