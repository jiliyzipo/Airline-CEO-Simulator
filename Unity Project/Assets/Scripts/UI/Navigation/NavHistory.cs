using System.Collections.Generic;

namespace AirlineCEO.UI.Navigation
{
    /// <summary>FM 式浏览历史：后退 / 前进。</summary>
    public class NavHistory
    {
        readonly List<string> _items = new List<string>();
        int _index = -1;

        public bool CanBack => _index > 0;
        public bool CanForward => _index >= 0 && _index < _items.Count - 1;
        public string Current => _index >= 0 && _index < _items.Count ? _items[_index] : null;

        /// <summary>
        /// 当前页之前的那一格 —— 用户**真正来的地方**。
        /// 返回时用来判断「弹出来的这个部门页是不是用户去过的地方」：见
        /// <c>PrefabNavIntegration.GoBack</c>，深链为安放子页临时切过去的部门页不算。
        /// </summary>
        public string Previous => _index > 0 && _index - 1 < _items.Count ? _items[_index - 1] : null;

        /// <summary>清空整条浏览历史：退出到开始界面时用，否则下一局的后退会翻到上一局的页。</summary>
        public void Clear()
        {
            _items.Clear();
            _index = -1;
        }

        public void Record(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) return;
            if (_index >= 0 && _index < _items.Count && _items[_index] == pageId)
                return;
            if (_index < _items.Count - 1)
                _items.RemoveRange(_index + 1, _items.Count - _index - 1);
            _items.Add(pageId);
            _index = _items.Count - 1;
        }

        public bool MoveBack()
        {
            if (!CanBack) return false;
            _index--;
            return true;
        }

        public bool MoveForward()
        {
            if (!CanForward) return false;
            _index++;
            return true;
        }
    }
}
