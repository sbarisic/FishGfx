#version 400

in vec4 frag_Clr;
in vec2 frag_UV;
in vec3 frag_Normal;
in vec3 frag_WorldPosition;
in vec4 frag_Light;

layout (location = 0) out vec4 OutColor;

uniform sampler2D Texture0;
uniform vec3 LightDirection;
uniform float AmbientLight;
uniform vec3 SunColor;
uniform float SunIntensity;
uniform float AlphaCutoff;
uniform vec3 ViewPos;
uniform int FogEnabled;
uniform vec3 FogColor;
uniform float FogDensity;
uniform float LightMultiplier;

void main()
{
	vec4 sampled = texture(Texture0, frag_UV) * frag_Clr;

	if (AlphaCutoff >= 0.0 && sampled.a < AlphaCutoff)
		discard;

	float diffuse = max(dot(normalize(frag_Normal), normalize(-LightDirection)), 0.0);
	float directional = AmbientLight + diffuse * (1.0 - AmbientLight);
	vec3 skyLight = frag_Light.a * SunColor * SunIntensity * directional;
	vec3 lighting = max(frag_Light.rgb, skyLight);
	vec3 litColor = sampled.rgb * lighting * LightMultiplier;
	float fogFactor = FogEnabled != 0
		? clamp(1.0 - exp(-FogDensity * distance(ViewPos, frag_WorldPosition)), 0.0, 1.0)
		: 0.0;
	OutColor = vec4(mix(litColor, FogColor, fogFactor), sampled.a);
}
