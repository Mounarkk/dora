Shader "Custom/ObjectIDShader"
{
    Properties
    {
        _ObjectColorR ("Object Color Red", Int) = 0
        _ObjectColorG ("Object Color Green", Int) = 0
        _ObjectColorB ("Object Color Blue", Int) = 0
        _IsBackground ("Is Background", Int) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
            };
            
            struct v2f
            {
                float4 vertex : SV_POSITION;
            };
            
            int _IsBackground;
            int _ObjectColorR;
            int _ObjectColorG;
            int _ObjectColorB;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                if (_IsBackground == 1)
                {
                    return fixed4(0.0, 0.0, 0.0, 1.0);
                }
                
                // Convert integers to normalized float values (0-255 -> 0-1)
                return fixed4(
                    _ObjectColorR / 255.0,
                    _ObjectColorG / 255.0,
                    _ObjectColorB / 255.0,
                    1.0
                );
            }
            ENDCG
        }
    }
}