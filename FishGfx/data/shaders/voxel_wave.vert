#version 430

layout (location = 0) in vec3 Pos;
layout (location = 1) in vec4 Clr;
layout (location = 2) in vec2 UV;
layout (location = 3) in vec3 Normal;
layout (location = 4) in vec4 Tangent;
layout (location = 5) in vec4 Wave;
layout (location = 6) in vec4 Light;

out vec4 frag_Clr;
out vec2 frag_UV;
out vec3 frag_Normal;
out vec4 frag_Tangent;
out vec3 frag_WorldPosition;
out vec4 frag_Light;
out float frag_WaveAmplitude;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
uniform float uTime;

const vec2 WaveDirectionA = vec2(0.943858, 0.330350);
const vec2 WaveDirectionB = vec2(-0.371391, 0.928477);

void main()
{
	vec4 worldPosition = uModel * vec4(Pos, 1.0);
	vec3 worldNormal = normalize(mat3(uModel) * Normal);

	if (Wave.w > 0.0 && Wave.x > 0.0)
	{
		float phaseA = dot(worldPosition.xz, WaveDirectionA) * Wave.y + uTime * Wave.z;
		float phaseB = dot(worldPosition.xz, WaveDirectionB) * (Wave.y * 0.73) - uTime * (Wave.z * 1.17);
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
	vec3 transformedTangent = mat3(uModel) * Tangent.xyz;
	float tangentLengthSquared = dot(transformedTangent, transformedTangent);
	frag_Tangent = tangentLengthSquared > 0.0000001
		? vec4(transformedTangent * inversesqrt(tangentLengthSquared), Tangent.w)
		: vec4(0.0);
	frag_WorldPosition = worldPosition.xyz;
	frag_Light = Light;
	frag_WaveAmplitude = Wave.x;
	gl_Position = uProjection * uView * worldPosition;
}
