#version 400

uniform vec2 uViewport;
uniform float uThickness;

layout (lines) in;
layout (triangle_strip, max_vertices = 4) out;

in vec4 vColor[];

out vec4 gColor;

void main()
{
	vec2 start = gl_in[0].gl_Position.xy / gl_in[0].gl_Position.w;
	vec2 end = gl_in[1].gl_Position.xy / gl_in[1].gl_Position.w;
	vec2 direction = start - end;
	direction = normalize(vec2(direction.x, direction.y * uViewport.y / uViewport.x));
	vec2 tangent = vec2(-direction.y, direction.x) * uThickness / uViewport;

	gl_Position = vec4(
		(start - tangent) * gl_in[0].gl_Position.w,
		gl_in[0].gl_Position.zw
	);
	gColor = vColor[0];
	EmitVertex();

	gl_Position = vec4(
		(start + tangent) * gl_in[0].gl_Position.w,
		gl_in[0].gl_Position.zw
	);
	gColor = vColor[0];
	EmitVertex();

	gl_Position = vec4(
		(end - tangent) * gl_in[1].gl_Position.w,
		gl_in[1].gl_Position.zw
	);
	gColor = vColor[1];
	EmitVertex();

	gl_Position = vec4(
		(end + tangent) * gl_in[1].gl_Position.w,
		gl_in[1].gl_Position.zw
	);
	gColor = vColor[1];
	EmitVertex();
	EndPrimitive();
}
