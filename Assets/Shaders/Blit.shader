Shader "Hidden/MRT Blit"
{
    Properties
    {
    }

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

    Varyings vertex(in Input input)
    {
        Varyings output;

        output.vertex = input.vertex;
        output.uv = .5 * input.vertex.xy + .5;

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
