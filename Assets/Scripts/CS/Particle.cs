// burst compileを使う前提のパーティクルクラス

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace PBD
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Particle
    {
        public float3 position;
        public float life;
        public float3 velocity;
        public float mass;
    }
}