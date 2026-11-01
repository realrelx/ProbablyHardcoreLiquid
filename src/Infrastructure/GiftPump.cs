using System;
using UnityEngine;

namespace ProbablyHardcoreLiquid
{
    // 落位重试泵：背包窗口关着时 invElement 可能是锁定/未激活的，
    // 每秒试一次，直到放进去（或 10 分钟后放弃）。只做重试，不做别的逻辑。
    internal sealed class GiftPump : MonoBehaviour
    {
        private float _next;
        private float _deadline;

        private void Awake()
        {
            _deadline = Time.realtimeSinceStartup + 600f;
            _next = Time.realtimeSinceStartup + 0.5f;
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _next) return;
            _next = Time.realtimeSinceStartup + 1f;
            if (Time.realtimeSinceStartup > _deadline)
            {
                Spray.StopPump();
                return;
            }
            try
            {
                Spray.PumpTick();
            }
            catch (Exception)
            {
            }
        }
    }
}
