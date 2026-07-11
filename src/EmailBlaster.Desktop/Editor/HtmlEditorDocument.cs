namespace EmailBlaster.Desktop.Editor;

/// <summary>
/// A fully self-contained rich-text editor rendered inside WebView2. No external resources are
/// referenced (CDN-free), so it works offline. The host talks to it through three global JS functions
/// — <c>setHtml</c>, <c>getHtml</c>, <c>insertHtml</c> — and receives live content updates via
/// <c>window.chrome.webview.postMessage</c>.
/// </summary>
internal static class HtmlEditorDocument
{
    public const string Html =
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <style>
          * { box-sizing: border-box; }
          html, body { height: 100%; margin: 0; }
          body {
            font-family: 'Segoe UI', system-ui, sans-serif;
            display: flex; flex-direction: column;
            color: #0f172a; background: #ffffff;
          }
          #toolbar {
            display: flex; flex-wrap: wrap; align-items: center; gap: 2px;
            padding: 8px 10px; border-bottom: 1px solid #e2e8f0;
            background: #f8fafc; position: sticky; top: 0; z-index: 5;
          }
          .tbtn {
            min-width: 30px; height: 30px; padding: 0 8px;
            border: 1px solid transparent; border-radius: 6px;
            background: transparent; color: #334155; cursor: pointer;
            font-size: 14px; display: inline-flex; align-items: center; justify-content: center;
          }
          .tbtn:hover { background: #e2e8f0; }
          .tbtn.wide { font-size: 12.5px; }
          .sep { width: 1px; height: 20px; background: #e2e8f0; margin: 0 6px; }
          .swatch { width: 18px; height: 18px; border-radius: 4px; margin: 0 2px; cursor: pointer;
                    border: 1px solid rgba(0,0,0,.12); }
          #linkbar {
            display: none; align-items: center; gap: 6px; width: 100%;
            padding: 6px 4px 0; }
          #linkbar input {
            flex: 1; height: 28px; padding: 0 8px; font-size: 13px;
            border: 1px solid #cbd5e1; border-radius: 6px; outline: none;
          }
          #linkbar input:focus { border-color: #2563eb; }
          #editor {
            flex: 1; overflow-y: auto; padding: 18px 22px; outline: none;
            font-size: 14px; line-height: 1.55;
          }
          #editor:empty:before { content: attr(data-placeholder); color: #94a3b8; }
          #editor p { margin: 0 0 10px; }
          #editor h1 { font-size: 22px; margin: 4px 0 10px; }
          #editor h2 { font-size: 18px; margin: 4px 0 10px; }
          #editor a { color: #2563eb; }
          #editor img { max-width: 100%; }
        </style>
        </head>
        <body>
          <div id="toolbar">
            <button class="tbtn" title="Bold" data-cmd="bold"><b>B</b></button>
            <button class="tbtn" title="Italic" data-cmd="italic"><i>I</i></button>
            <button class="tbtn" title="Underline" data-cmd="underline"><u>U</u></button>
            <button class="tbtn" title="Strikethrough" data-cmd="strikeThrough"><s>S</s></button>
            <span class="sep"></span>
            <button class="tbtn wide" title="Heading 1" data-block="h1">H1</button>
            <button class="tbtn wide" title="Heading 2" data-block="h2">H2</button>
            <button class="tbtn wide" title="Paragraph" data-block="p">&para;</button>
            <span class="sep"></span>
            <button class="tbtn" title="Bulleted list" data-cmd="insertUnorderedList">&#8226;</button>
            <button class="tbtn" title="Numbered list" data-cmd="insertOrderedList">1.</button>
            <span class="sep"></span>
            <button class="tbtn" title="Align left" data-cmd="justifyLeft">&#8801;</button>
            <button class="tbtn" title="Align center" data-cmd="justifyCenter">&#8803;</button>
            <button class="tbtn" title="Align right" data-cmd="justifyRight">&#8802;</button>
            <span class="sep"></span>
            <button class="tbtn" title="Insert link" id="linkBtn">&#128279;</button>
            <button class="tbtn" title="Remove link" data-cmd="unlink">&#128683;</button>
            <span class="sep"></span>
            <span class="swatch" style="background:#0f172a" data-color="#0f172a"></span>
            <span class="swatch" style="background:#2563eb" data-color="#2563eb"></span>
            <span class="swatch" style="background:#16a34a" data-color="#16a34a"></span>
            <span class="swatch" style="background:#dc2626" data-color="#dc2626"></span>
            <span class="swatch" style="background:#d97706" data-color="#d97706"></span>
            <span class="sep"></span>
            <button class="tbtn" title="Clear formatting" data-cmd="removeFormat">&#10005;</button>
            <div id="linkbar">
              <input id="linkInput" type="text" placeholder="https://example.com  (Enter to apply, Esc to cancel)">
            </div>
          </div>
          <div id="editor" contenteditable="true"
               data-placeholder="Write your message here…"></div>

        <script>
          var editor = document.getElementById('editor');
          var savedRange = null;

          function saveSelection() {
            var sel = window.getSelection();
            if (sel && sel.rangeCount > 0) {
              var r = sel.getRangeAt(0);
              if (editor.contains(r.commonAncestorContainer)) savedRange = r.cloneRange();
            }
          }
          function restoreSelection() {
            if (!savedRange) { editor.focus(); return; }
            var sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(savedRange);
          }

          function exec(cmd, value) {
            restoreSelection();
            document.execCommand(cmd, false, value || null);
            editor.focus();
            saveSelection();
            notify();
          }
          function formatBlock(tag) { exec('formatBlock', tag); }

          function notify() {
            if (window.chrome && window.chrome.webview) {
              window.chrome.webview.postMessage(JSON.stringify({ type: 'html', value: editor.innerHTML }));
            }
          }

          // ---- Host-facing API ----
          function setHtml(html) { editor.innerHTML = html || ''; saveSelection(); }
          function getHtml() { return editor.innerHTML; }
          function insertHtml(html) {
            restoreSelection();
            editor.focus();
            document.execCommand('insertHTML', false, html);
            saveSelection();
            notify();
          }

          // ---- Toolbar wiring ----
          document.getElementById('toolbar').addEventListener('mousedown', function (e) {
            // Keep the editor selection when clicking toolbar controls.
            if (e.target.closest('#linkbar')) return;
            e.preventDefault();
          });

          document.querySelectorAll('.tbtn[data-cmd]').forEach(function (b) {
            b.addEventListener('click', function () { exec(b.getAttribute('data-cmd')); });
          });
          document.querySelectorAll('.tbtn[data-block]').forEach(function (b) {
            b.addEventListener('click', function () { formatBlock(b.getAttribute('data-block')); });
          });
          document.querySelectorAll('.swatch').forEach(function (s) {
            s.addEventListener('mousedown', function (e) { e.preventDefault(); });
            s.addEventListener('click', function () { exec('foreColor', s.getAttribute('data-color')); });
          });

          // ---- Link bar ----
          var linkbar = document.getElementById('linkbar');
          var linkInput = document.getElementById('linkInput');
          document.getElementById('linkBtn').addEventListener('click', function () {
            saveSelection();
            linkbar.style.display = 'flex';
            linkInput.value = 'https://';
            linkInput.focus();
            linkInput.select();
          });
          linkInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
              e.preventDefault();
              var url = linkInput.value.trim();
              linkbar.style.display = 'none';
              if (url) exec('createLink', url);
            } else if (e.key === 'Escape') {
              e.preventDefault();
              linkbar.style.display = 'none';
              editor.focus();
            }
          });

          // ---- Content sync (debounced) ----
          var timer = null;
          editor.addEventListener('input', function () {
            clearTimeout(timer);
            timer = setTimeout(notify, 250);
          });
          editor.addEventListener('keyup', saveSelection);
          editor.addEventListener('mouseup', saveSelection);
          editor.addEventListener('blur', saveSelection);
        </script>
        </body>
        </html>
        """;
}
