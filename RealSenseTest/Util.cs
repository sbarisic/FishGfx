using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace RealSenseTest {
	static class Util {
		static Util() {
			var Method = new DynamicMethod("Memset", MethodAttributes.Public | MethodAttributes.Static, CallingConventions.Standard,
				null, new[] { typeof(IntPtr), typeof(byte), typeof(int) }, typeof(Util), true);

			var Gen = Method.GetILGenerator();
			Gen.Emit(OpCodes.Ldarg_0);
			Gen.Emit(OpCodes.Ldarg_1);
			Gen.Emit(OpCodes.Ldarg_2);
			Gen.Emit(OpCodes.Initblk);
			Gen.Emit(OpCodes.Ret);

			MemSet = (Action<IntPtr, byte, int>)Method.CreateDelegate(typeof(Action<IntPtr, byte, int>));
		}

		public static readonly Action<IntPtr, byte, int> MemSet;
	}
}
