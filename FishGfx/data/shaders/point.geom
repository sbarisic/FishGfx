#version 400

uniform vec2 uViewport;
uniform float uThickness;

layout (points) in;
layout (triangle_strip, max_vertices = 4) out;

in vec4 vColor[];

out vec4 gColor;
out vec2 gCenterOffset;
out float gScaledThickness;

void main()
{
	float scaledThickness = uThickness;
	vec2 position = gl_in[0].gl_Position.xy / gl_in[0].gl_Position.w;
	vec2 radius = vec2(scaledThickness) / uViewport;

	gl_Position = vec4(position + radius * vec2(-1.0, -1.0), gl_in[0].gl_Position.zw);
	gColor = vColor[0];
	gCenterOffset = vec2(-1.0, -1.0);
	gScaledThickness = scaledThickness;
	EmitVertex();

	gl_Position = vec4(position + radius * vec2(-1.0, 1.0), gl_in[0].gl_Position.zw);
	gColor = vColor[0];
	gCenterOffset = vec2(-1.0, 1.0);
	gScaledThickness = scaledThickness;
	EmitVertex();

	gl_Position = vec4(position + radius * vec2(1.0, -1.0), gl_in[0].gl_Position.zw);
	gColor = vColor[0];
	gCenterOffset = vec2(1.0, -1.0);
	gScaledThickness = scaledThickness;
	EmitVertex();

	gl_Position = vec4(position + radius * vec2(1.0, 1.0), gl_in[0].gl_Position.zw);
	gColor = vColor[0];
	gCenterOffset = vec2(1.0, 1.0);
	gScaledThickness = scaledThickness;
	EmitVertex();

	EndPrimitive();
}
