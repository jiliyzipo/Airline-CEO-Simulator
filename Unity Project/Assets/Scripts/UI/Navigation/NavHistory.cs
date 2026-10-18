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
