using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishGfx {
	public static class MicroConfig {
		public static string Serialize(object Obj) {
			if (Obj == null)
				return "null";

			Type ObjType = Obj.GetType();

			if (ObjType.IsEnum)
				return Enum.GetName(ObjType, Obj);

			Type[] ToStringable = new Type[] {
				 typeof(int), typeof(uint), typeof(short), typeof(ushort), typeof(byte), typeof(sbyte)
			 };

			if (ToStringable.Contains(ObjType))
				return Obj.ToString();
			else if (ObjType == typeof(float))
				return ((float)Obj).ToString(CultureInfo.InvariantCulture);
			else if (ObjType == typeof(double))
				return ((double)Obj).ToString(CultureInfo.InvariantCulture);
			else if (ObjType == typeof(string))
				return string.Format("\"{0}\"", Obj.ToString().Replace("\"", "\\\""));
			else if (ObjType == typeof(bool))
				return ObjType.ToString().ToLower();

			throw new Exception("Unsupported type " + Obj.GetType());
		}

		public static object Deserialize(string Str, Type T) {
			Str = Str.Trim();

			if (!T.IsValueType)
				if (Str.ToLower() == "null")
					return null;
				else
					throw new Exception("Cannot cast null to value type");

			if (T.IsEnum) {
				if (int.TryParse(Str, out int EnumInt))
					return Convert.ChangeType(EnumInt, T);

				return Enum.Parse(T, Str, true);
			}

			if (T == typeof(int))
				return int.Parse(Str, CultureInfo.InvariantCulture);
			else if (T == typeof(uint))
				return uint.Parse(Str, CultureInfo.InvariantCulture);
			else if (T == typeof(float))
				return float.Parse(Str, CultureInfo.InvariantCulture);
			else if (T == typeof(double))
				return double.Parse(Str, CultureInfo.InvariantCulture);
			else if (T == typeof(byte))
				return byte.Parse(Str, CultureInfo.InvariantCulture);
			else if (T == typeof(ushort))
				return ushort.Parse(Str, CultureInfo.InvariantCulture);
			else if (T == typeof(short))
				return short.Parse(Str, CultureInfo.InvariantCulture);
			else if (T == typeof(string))
				return Str.Substring(1, Str.Length - 2).Replace("\\\"", "\"");
			else if (T == typeof(bool))
				return bool.Parse(Str);

			throw new Exception("Unknown type " + T);
		}

		public static string Serialize(object[] Keys, object[] Values) {
			if (Keys.Length != Values.Length)
				throw new Exception("Length of keys does not match length of values");

			StringBuilder SB = new StringBuilder();

			for (int i = 0; i < Keys.Length; i++) {
				SB.Append(Serialize(Keys[i]));
				SB.Append(" = ");
				SB.AppendLine(Serialize(Values[i]));
			}

			return SB.ToString();
		}

		public static void Deserialize(string Data, Type KeyType, Type ValueType, out object[] Keys, out object[] Values) {
			string[] Lines = Data.Trim().Split('\n').Select(L => L.Trim()).Where(L => L.Length > 0).ToArray();
			Keys = new object[Lines.Length];
			Values = new object[Lines.Length];

			for (int i = 0; i < Lines.Length; i++) {
				int IdxOfEq = Lines[i].IndexOf('=');

				string Key = Lines[i].Substring(0, IdxOfEq).Trim();
				string Val = Lines[i].Substring(IdxOfEq + 1, Lines[i].Length - IdxOfEq - 1).Trim();

				Keys[i] = Deserialize(Key, KeyType);
				Values[i] = Deserialize(Val, ValueType);
			}
		}
	}
}
