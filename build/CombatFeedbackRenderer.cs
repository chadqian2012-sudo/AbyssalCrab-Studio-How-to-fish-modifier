using System;
using UnityEngine;

namespace AbyssalCrab.Runtime
{
    public sealed class CombatFeedbackRenderer : MonoBehaviour
    {
        public static Action Render;
        private void OnGUI() { if (Render != null) Render(); }
    }
}
