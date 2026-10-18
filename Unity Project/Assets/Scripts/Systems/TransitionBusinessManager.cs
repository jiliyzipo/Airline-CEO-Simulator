using AirlineCEO.Data;
using AirlineCEO.Logic;
using AirlineCEO.Systems;

namespace AirlineCEO.Systems
{
    /// <summary>接管开局的过渡业务种子：合同已在 TakeoverWorldBuilder 写入，此处只跳过「开航前请示」。</summary>
    public static class TransitionBusinessManager
    {
        public static void OnTakeoverReady(GameState gs)
        {
            if (gs == null) return;
            gs.transitionIntroSent = true;
            TransitionBusinessLogic.Ensure(gs);
        }
    }
}
