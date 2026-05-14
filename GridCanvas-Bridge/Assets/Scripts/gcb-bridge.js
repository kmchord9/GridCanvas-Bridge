'use strict';

const GCB = (() => {
  const COLS = 24;
  const ROWS = 24;

  let selectedEl = null;
  let editingEl = null;
  let dragState = null;

  // グリッド座標 → CSS % 変換
  const toPctX = (g) => (g / COLS * 100).toFixed(4) + '%';
  const toPctY = (g) => (g / ROWS * 100).toFixed(4) + '%';

  function snapToGrid(px, containerSize, cells) {
    const cellSize = containerSize / cells;
    return Math.max(0, Math.min(cells - 1, Math.round(px / cellSize)));
  }

  function applyGridPos(el, gridX, gridY, gridW, gridH) {
    el.style.left   = toPctX(gridX);
    el.style.top    = toPctY(gridY);
    el.style.width  = toPctX(gridW);
    el.style.height = toPctY(gridH);
    el.dataset.gridX = gridX;
    el.dataset.gridY = gridY;
    el.dataset.gridW = gridW;
    el.dataset.gridH = gridH;
  }

  function postMessage(msg) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(msg);
    }
  }

  // ── 選択 ───────────────────────────────────────────────

  function selectElement(el) {
    if (selectedEl) selectedEl.classList.remove('selected');
    selectedEl = el;
    if (el) {
      el.classList.add('selected');
      postMessage({
        type: 'elementSelected',
        id: el.id,
        gridX: +el.dataset.gridX,
        gridY: +el.dataset.gridY,
        gridW: +el.dataset.gridW,
        gridH: +el.dataset.gridH,
      });
    } else {
      postMessage({ type: 'elementDeselected' });
    }
  }

  // ── テキスト編集モード ─────────────────────────────────

  function enterEditMode(el, clientX, clientY) {
    if (editingEl === el) return;
    exitEditMode();

    editingEl = el;
    el.contentEditable = 'true';
    el.classList.add('editing');
    el.focus();

    // クリック座標にカーソルを配置 (Chromium / WebView2)
    if (clientX !== undefined && document.caretRangeFromPoint) {
      const range = document.caretRangeFromPoint(clientX, clientY);
      if (range) {
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);
      }
    }
  }

  function exitEditMode() {
    if (!editingEl) return;
    const el = editingEl;
    editingEl = null;
    el.contentEditable = 'false';
    el.classList.remove('editing');
    postMessage({ type: 'contentChanged', id: el.id, content: el.innerText.trim() });
  }

  // ── ドラッグ & ドロップ ────────────────────────────────

  function onMouseDown(e) {
    const el = e.currentTarget;

    // 編集中はブラウザのテキスト操作に任せる
    if (el.contentEditable === 'true') return;

    // 子要素クリック (e.g. テキストノード) は選択のみ
    if (e.target !== el) {
      selectElement(el);
      return;
    }

    e.preventDefault();
    selectElement(el);

    const root = document.getElementById('slide-root');
    const rect  = root.getBoundingClientRect();
    dragState = {
      el,
      startMouseX: e.clientX,
      startMouseY: e.clientY,
      startGridX:  +el.dataset.gridX,
      startGridY:  +el.dataset.gridY,
      gridW:        +el.dataset.gridW,
      gridH:        +el.dataset.gridH,
      rootW: rect.width,
      rootH: rect.height,
    };
    el.classList.add('dragging');
  }

  function onMouseMove(e) {
    if (!dragState) return;
    const { el, startMouseX, startMouseY, startGridX, startGridY, gridW, gridH, rootW, rootH } = dragState;

    const dx = e.clientX - startMouseX;
    const dy = e.clientY - startMouseY;
    const newGridX = Math.max(0, Math.min(COLS - gridW, snapToGrid(startGridX * (rootW / COLS) + dx, rootW, COLS)));
    const newGridY = Math.max(0, Math.min(ROWS - gridH, snapToGrid(startGridY * (rootH / ROWS) + dy, rootH, ROWS)));
    applyGridPos(el, newGridX, newGridY, gridW, gridH);
  }

  function onMouseUp() {
    if (!dragState) return;
    const { el } = dragState;
    el.classList.remove('dragging');
    postMessage({ type: 'elementMoved', id: el.id, gridX: +el.dataset.gridX, gridY: +el.dataset.gridY });
    dragState = null;
  }

  // ── イベントハンドラ ────────────────────────────────────

  function onDblClick(e) {
    e.stopPropagation();
    enterEditMode(e.currentTarget, e.clientX, e.clientY);
  }

  function onFocusOut(e) {
    // フォーカス移動先が同じ要素内なら継続
    if (e.currentTarget.contains(e.relatedTarget)) return;
    exitEditMode();
  }

  // ── Public API ──────────────────────────────────────────

  return {
    enableEditMode() {
      const root = document.getElementById('slide-root');
      if (!root) return;
      root.classList.add('edit-mode');
      // 編集中はプレゼンナビゲーション (クリックゾーン・ボタン) を無効化
      document.getElementById('gcb-presentation')?.classList.add('gcb-editing');

      document.querySelectorAll('.gcb-element').forEach((el) => {
        el.addEventListener('mousedown', onMouseDown);
        el.addEventListener('dblclick',  onDblClick);
        el.addEventListener('focusout',  onFocusOut);
      });

      document.addEventListener('mousemove', onMouseMove);
      document.addEventListener('mouseup',   onMouseUp);

      // スライド背景クリック: 編集・選択を解除
      root.addEventListener('mousedown', (e) => {
        if (e.target === root) {
          exitEditMode();
          selectElement(null);
        }
      });

      // Escape で編集モード終了
      document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') exitEditMode();
      });
    },

    moveElement(id, gridX, gridY) {
      const el = document.getElementById(id);
      if (!el) return;
      applyGridPos(el, gridX, gridY, +el.dataset.gridW, +el.dataset.gridH);
    },

    resizeElement(id, gridW, gridH) {
      const el = document.getElementById(id);
      if (!el) return;
      applyGridPos(el, +el.dataset.gridX, +el.dataset.gridY, gridW, gridH);
    },
  };
})();
