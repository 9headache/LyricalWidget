// Barrel distortion for the widget's Moonlight Bloom stage.
// Compiled to WidgetFisheye.ps (ps_2_0, entry point main), e.g. fxc /T ps_2_0 /E main /Fo WidgetFisheye.ps WidgetFisheye.fx
//
// Same mapping as Rendering/FisheyeLens.cs: a source point at radius t shows at the radius r where
// t = r * (1 - k + k * r^2), so each output pixel reads its source directly, with no mesh and no iteration.

sampler2D input : register(s0);
float strength : register(c0);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = (uv * 2.0) - 1.0;
    float2 source = (p * (1.0 - strength + (strength * dot(p, p))) * 0.5) + 0.5;

    // The corners reach past the stage: they stay empty instead of smearing its edge pixels
    float2 inside = step(0.0, source) * step(source, 1.0);
    return tex2D(input, source) * inside.x * inside.y;
}
