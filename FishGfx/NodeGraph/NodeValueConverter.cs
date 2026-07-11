using System;
using System.Globalization;
using System.Numerics;

namespace FishGfx.NodeGraph
{
	public static class NodeValueConverter
	{
		private static readonly Type[] Supported =
		{
			typeof(string),
			typeof(bool),
			typeof(byte),
			typeof(sbyte),
			typeof(short),
			typeof(ushort),
			typeof(int),
			typeof(uint),
			typeof(long),
			typeof(ulong),
			typeof(float),
			typeof(double),
			typeof(decimal),
			typeof(Vector2),
			typeof(Vector3),
			typeof(Vector4),
		};

		public static bool IsSupported(Type type) =>
			type != null && (type.IsEnum || Array.IndexOf(Supported, type) >= 0);

		public static string TypeName(Type type) =>
			type == typeof(Vector2) ? "Vector2"
			: type == typeof(Vector3) ? "Vector3"
			: type == typeof(Vector4) ? "Vector4"
			: type.Name;

		public static string Format(object value, Type type)
		{
			if (value == null)
				return "";
			if (value is Vector2 v2)
				return $"{F(v2.X)}, {F(v2.Y)}";
			if (value is Vector3 v3)
				return $"{F(v3.X)}, {F(v3.Y)}, {F(v3.Z)}";
			if (value is Vector4 v4)
				return $"{F(v4.X)}, {F(v4.Y)}, {F(v4.Z)}, {F(v4.W)}";
			return value is IFormattable formattable
				? formattable.ToString(null, CultureInfo.InvariantCulture)
				: value.ToString();
		}

		public static bool TryParse(string text, Type type, out object value)
		{
			value = null;
			if (!IsSupported(type))
				return false;
			if (type == typeof(string))
			{
				value = text ?? "";
				return true;
			}
			if (type.IsEnum)
			{
				try
				{
					value = Enum.Parse(type, text, true);
					return true;
				}
				catch
				{
					return false;
				}
			}
			if (type == typeof(bool))
			{
				bool ok = bool.TryParse(text, out bool v);
				value = v;
				return ok;
			}
			if (type == typeof(Vector2))
				return TryVector(text, 2, a => new Vector2(a[0], a[1]), out value);
			if (type == typeof(Vector3))
				return TryVector(text, 3, a => new Vector3(a[0], a[1], a[2]), out value);
			if (type == typeof(Vector4))
				return TryVector(text, 4, a => new Vector4(a[0], a[1], a[2], a[3]), out value);
			try
			{
				value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
				if ((value is float f && !float.IsFinite(f)) || (value is double d && !double.IsFinite(d)))
				{
					value = null;
					return false;
				}
				return true;
			}
			catch
			{
				value = null;
				return false;
			}
		}

		public static object Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

		private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

		private static bool TryVector(string text, int count, Func<float[], object> create, out object value)
		{
			value = null;
			string[] parts = (text ?? "").Split(',');
			if (parts.Length != count)
				return false;
			float[] numbers = new float[count];
			for (int i = 0; i < count; i++)
				if (
					!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i])
					|| !float.IsFinite(numbers[i])
				)
					return false;
			value = create(numbers);
			return true;
		}
	}
}
