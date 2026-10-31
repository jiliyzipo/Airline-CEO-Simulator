using UnityEngine;

namespace AirlineCEO.UI
{
    /// <summary>
    /// **已废弃**（2026-10-30）：航班环改由运营部后台编成，页面换成只读的
    /// <see cref="OpsRotationBoardUI"/>（甘特时间轴 + 六步留痕 + 运力缺口）。
    ///
    /// 这里保留一个空壳类而不是删文件，是为了不把已烘焙的
    /// <c>Prefabs/UI/Panels/Panel_OPS_n36.prefab</c> 弄成「Missing Script」——
    /// 那会让整页报警告，也让 Codex 的离线检查多一条噪音。
    /// 旧页里的「点航段 / 自动串联 / 校验连班」全部搬到了运营部（<c>OpsRotationBuilder</c>），
    /// CEO 侧不再保留任何手工拼环入口。
    /// </summary>
    public class OpsRingBuilderUI : MonoBehaviour
    {
        void Awake()
        {
            // 预置体里这一层还挂着旧版烘焙好的文案与按钮：直接收起，别再露出来。
            gameObject.SetActive(false);
        }
    }
}
