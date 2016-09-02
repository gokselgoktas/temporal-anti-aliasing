Shader "Hidden/MRT Blit"
{
    CGINCLUDE
    struct Input
    {
        float4 vertex : POSITION;
    };

    struct Varyings
    {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Output
    {
        float4 first : SV_Target0;
        float4 second : SV_Target1;
    };

    sampler2D _BlitSourceTex;
    float4 _BlitSourceTex_TexelSize;

    Varyings vertex(in Input input)
    {
        Varyings output;

        output.vertex = input.vertex;

        float2 uv = .5 * input.vertex.xy + .5;
        output.uv = uv;

    #if UNITY_UV_STARTS_AT_TOP
        //if (_BlitSourceTex_TexelSize.y < 0.)
            output.uv.y = 1. - uv.y;
    #endif

        return output;
    }

    Output fragment(in Varyings input)
    {
        Output output;

        float4 color = tex2D(_BlitSourceTex, input.uv);

        output.first = color;
        output.second = color;

        return output;
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vertex
            #pragma fragment fragment
            ENDCG
        }
    }
}
