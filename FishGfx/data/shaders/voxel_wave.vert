#version 400

layout (location = 0) in vec3 Pos;
layout (location = 1) in vec4 Clr;
layout (location = 2) in vec2 UV;
layout (location = 3) in vec3 Normal;
layout (location = 4) in vec4 Wave;

out vec4 frag_Clr;
out vec2 frag_UV;
out vec3 frag_Normal;
out vec3 frag_WorldPosition;

uniform mat4 Model;
uniform mat4 View;
uniform mat4 Project;
uniform float Time;

const vec2 WaveDirectionA = vec2(0.943858, 0.330350);
const vec2 WaveDirectionB = vec2(-0.371391, 0.928477);

void main()
{
	vec4 worldPosition = Model * vec4(Pos, 1.0);
	vec3 worldNormal = normalize(mat3(Model) * Normal);

	if (Wave.w > 0.0 && Wave.x > 0.0)
	{
		float phaseA = dot(worldPosition.xz, WaveDirectionA) * Wave.y + Time * Wave.z;
		float phaseB = dot(worldPosition.xz, WaveDirectionB) * (Wave.y * 0.73) - Time * (Wave.z * 1.17);
		float height = 0.5 * (sin(phaseA) + sin(phaseB));
		worldPosition.y += Wave.w * Wave.x * (height - 1.0);

		if (abs(worldNormal.y) > 0.5)
		{
			vec2 gradient = 0.5 * Wave.x * (
				cos(phaseA) * WaveDirectionA * Wave.y
				+ cos(phaseB) * WaveDirectionB * (Wave.y * 0.73)
			);
			vec3 surfaceNormal = normalize(vec3(-gradient.x, 1.0, -gradient.y));
			worldNormal = worldNormal.y < 0.0 ? -surfaceNormal : surfaceNormal;
		}
	}

	frag_Clr = Clr;
	frag_UV = UV;
	frag_Normal = worldNormal;
	frag_WorldPosition = worldPosition.xyz;
	gl_Position = Project * View * worldPosition;
}
