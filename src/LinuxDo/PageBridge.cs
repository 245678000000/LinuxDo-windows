using System.Text.Json;

namespace LinuxDo;

/// No cookies, account data, post bodies or draft text cross the bridge: only reading positions and a draft flag.
static class PageBridge
{
    static string? source;
    public static string Source => source ??= """
        (() => {
          if (window.top !== window) return;
          const allowed = /(^|\.)linux\.do$/i.test(location.hostname) || (PREVIEW && location.protocol === 'file:');
          if (!allowed || window.__linuxDoNative || !window.chrome || !window.chrome.webview) return;
          let timer, restoring = false, cancelled = false, lastDraft = false;
          const send = (type, data = {}) => window.chrome.webview.postMessage({type, ...data});
          function hasDraft() {
            return Array.from(document.querySelectorAll('.d-editor-input, #reply-control textarea, textarea[data-linuxdo-draft]'))
              .some(el => el.value.trim().length > 0 && el.getClientRects().length > 0);
          }
          function reportDraft() {
            const value = hasDraft();
            if (lastDraft !== value) { lastDraft = value; send('draft', {value}); }
          }
          function capture() {
            if (restoring) return;
            const posts = document.querySelectorAll('[data-post-id]');
            let anchor = null;
            for (const el of posts) { const r = el.getBoundingClientRect(); if (r.bottom > 0) { anchor = el; break; } }
            send('position', {url: location.href, x: Math.max(0, scrollX), y: Math.max(0, scrollY),
              postID: anchor ? anchor.getAttribute('data-post-id') : null,
              offset: anchor ? anchor.getBoundingClientRect().top : null});
          }
          function stopRestoring() { cancelled = true; restoring = false; }
          addEventListener('wheel', stopRestoring, {passive:true});
          addEventListener('pointerdown', stopRestoring, {passive:true});
          addEventListener('keydown', stopRestoring, {passive:true});
          addEventListener('scroll', () => { clearTimeout(timer); timer = setTimeout(capture, 650); }, {passive:true});
          addEventListener('input', reportDraft, {passive:true, capture:true});
          addEventListener('change', reportDraft, {passive:true, capture:true});
          addEventListener('pagehide', capture);
          document.addEventListener('visibilitychange', () => { if (document.hidden) capture(); });
          window.__linuxDoNative = {
            capture, hasDraft,
            configure(options) {
              const root = document.documentElement;
              if (!root) return;
              root.dataset.linuxdoReading = String(options.reading);
              root.dataset.linuxdoSidebar = String(options.sidebar);
              root.style.setProperty('--linuxdo-font', options.font + 'px');
              root.style.setProperty('--linuxdo-line', String(options.line));
            },
            restore(position) {
              if (cancelled || location.href !== position.url) return;
              restoring = true;
              let attempt = 0;
              function apply() {
                if (cancelled || location.href !== position.url) { restoring = false; return; }
                let target = position.y;
                if (position.postID) {
                  const anchor = Array.from(document.querySelectorAll('[data-post-id]')).find(el => el.getAttribute('data-post-id') === position.postID);
                  if (anchor) target = scrollY + anchor.getBoundingClientRect().top - (position.offset || 0);
                }
                scrollTo(position.x || 0, Math.max(0, target));
                if (++attempt < 4 && Math.abs(scrollY - target) > 20) setTimeout(apply, 350);
                else restoring = false;
              }
              apply();
            }
          };
          // The script runs at document creation; the head and body exist only after parsing.
          function install() {
            const style = document.createElement('style');
            style.id = 'linuxdo-native-reading';
            style.textContent = `
              html[data-linuxdo-reading="true"] .cooked {
                font-family: "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", sans-serif;
                font-size: var(--linuxdo-font, 16px); line-height: var(--linuxdo-line, 1.75);
                overflow-wrap: break-word;
              }
              html[data-linuxdo-reading="true"] .cooked p { line-height: inherit; }
              html[data-linuxdo-reading="true"] .cooked pre, html[data-linuxdo-reading="true"] .cooked code {
                font-family: "Cascadia Mono", Consolas, "Courier New", monospace;
              }
              html[data-linuxdo-sidebar="true"] .sidebar-wrapper { display: none !important; }
              html[data-linuxdo-sidebar="true"] body.has-sidebar-page #main-outlet-wrapper { grid-template-columns: minmax(0, 1fr) !important; }
              html[data-linuxdo-sidebar="true"] .header-sidebar-toggle { display: none !important; }
            `;
            (document.head || document.documentElement).appendChild(style);
            send('ready');
          }
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', install, {once: true});
          else install();
        })();
        """.Replace("PREVIEW", AppConfig.Preview ? "true" : "false");

    public static string ConfigureScript(bool sidebarVisible)
    {
        var s = Prefs.Current;
        var options = new
        {
            reading = s.Reading,
            sidebar = sidebarVisible && s.HideWebSidebar,
            font = Prefs.BoundedFont(s.FontSize),
            line = Prefs.BoundedLineHeight(s.LineHeight),
        };
        return $"window.__linuxDoNative?.configure({JsonSerializer.Serialize(options)});";
    }

    public static string RestoreScript(ReadingPosition p) =>
        $"window.__linuxDoNative?.restore({JsonSerializer.Serialize(new { url = p.Url, x = p.X, y = p.Y, postID = p.PostId, offset = p.Offset })});";
}
