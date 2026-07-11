#version 400

in vec4 frag_Clr;
in vec2 frag_UV;
in vec3 frag_Normal;

layout (location = 0) out vec4 OutColor;

uniform sampler2D Texture0;
uniform vec3 LightDirection;
uniform float AmbientLight;
uniform float AlphaCutoff;

void main()
{
	vec4 sampled = texture(Texture0, frag_UV) * frag_Clr;

	if (AlphaCutoff >= 0.0 && sampled.a < AlphaCutoff)
		discard;

	float diffuse = max(dot(normalize(frag_Normal), normalize(-LightDirection)), 0.0);
	float lighting = AmbientLight + diffuse * (1.0 - AmbientLight);
	OutColor = vec4(sampled.rgb * lighting, sampled.a);
}
