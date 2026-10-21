using System;
using System.Collections.Generic;

namespace AgoAnchor
{
    /// <summary>
    /// 多语言字符串表。所有面向用户的文案都从这里取。
    /// 每种语言一个平行的字符串数组，索引与 Lang 枚举一致：
    ///   0 = 简体中文, 1 = English, 2 = 日本語, 3 = 한국어
    /// 缺项自动回退到英文，避免切换语言后出现空白。
    /// </summary>
    public static class L
    {
        private static Lang current = Lang.Chinese;

        public static Lang Current
        {
            get { return current; }
            set { current = value; }
        }

        public static bool IsEnglish { get { return current == Lang.English; } }

        /// <summary>语言数量（供界面生成下拉框）。</summary>
        public const int LangCount = 4;

        private static readonly Dictionary<string, string[]> Table = BuildTable();

        public static string T(string key)
        {
            string[] row;
            if (!Table.TryGetValue(key, out row)) return key;

            int idx = (int)current;
            if (idx < 0 || idx >= row.Length) idx = (int)Lang.English;

            string v = row[idx];
            if (string.IsNullOrEmpty(v)) v = row[(int)Lang.English];   // 回退英文
            return v;
        }

        public static string T(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        /// <summary>语言在界面上的显示名（始终用该语言自己的写法）。</summary>
        public static string LangName(Lang lang)
        {
            switch (lang)
            {
                case Lang.English: return "English";
                case Lang.Japanese: return "日本語";
                case Lang.Korean: return "한국어";
                default: return "简体中文";
            }
        }

        /// <summary>按 Lang 枚举顺序取显示名。</summary>
        public static string LangNameAt(int index)
        {
            if (index < 0 || index >= LangCount) index = 0;
            return LangName((Lang)index);
        }

        private static void Add(Dictionary<string, string[]> t, string key,
                                string cn, string en, string jp, string kr)
        {
            t[key] = new[] { cn, en, jp, kr };
        }

        private static Dictionary<string, string[]> BuildTable()
        {
            var t = new Dictionary<string, string[]>(StringComparer.Ordinal);

            Add(t, "app.title",
                "AgoAnchor · 设置",
                "AgoAnchor · Settings",
                "AgoAnchor・設定",
                "AgoAnchor · 설정");

            // ---------- 分组 ----------
            Add(t, "grp.profile", "样式方案 Profile", "Profile", "プロファイル", "프로필");
            Add(t, "grp.grid", "点阵框", "Dot Frame", "ドットフレーム", "도트 프레임");
            Add(t, "grp.border", "边框（点阵与横线共用）", "Borders (dots & line)",
                "枠線（ドットと線で共通）", "테두리 (도트·선 공용)");
            Add(t, "grp.center", "中央横线（中间断开）", "Center Line (split)",
                "中央ライン（中央は分割）", "중앙 가로선 (중앙 분할)");
            Add(t, "grp.colors", "颜色", "Colors", "色", "색상");
            Add(t, "tab.grid", "点阵", "Dots", "ドット", "도트");
            Add(t, "tab.center", "中央线", "Center", "中央ライン", "중앙선");
            Add(t, "tab.xmode", "X Mode", "X Mode", "X モード", "X 모드");
            Add(t, "grp.preview", "预览", "Preview", "プレビュー", "미리보기");
            Add(t, "grp.screens", "显示屏幕（可多选）", "Displays (multi-select)",
                "表示ディスプレイ（複数選択可）", "표시 모니터 (다중 선택)");
            Add(t, "grp.system", "系统", "System", "システム", "시스템");

            // ---------- Profile ----------
            Add(t, "profile.current", "当前方案", "Profile", "現在のプロファイル", "현재 프로필");
            Add(t, "profile.add", "新建", "New", "新規", "새로 만들기");
            Add(t, "profile.rename", "重命名", "Rename", "名前を変更", "이름 변경");
            Add(t, "profile.delete", "删除", "Delete", "削除", "삭제");
            Add(t, "profile.name", "方案名称", "Profile name", "プロファイル名", "프로필 이름");
            Add(t, "profile.rename.title", "重命名方案", "Rename profile",
                "プロファイル名の変更", "프로필 이름 변경");
            Add(t, "profile.newname", "新方案", "New profile", "新しいプロファイル", "새 프로필");
            Add(t, "profile.default", "默认", "Default", "デフォルト", "기본");
            Add(t, "profile.p2", "方案 2", "Profile 2", "プロファイル 2", "프로필 2");
            Add(t, "profile.p3", "方案 3", "Profile 3", "プロファイル 3", "프로필 3");

            // ---------- 点阵 ----------
            Add(t, "grid.enabled", "启用点阵框", "Enable dot frame",
                "ドットフレームを有効化", "도트 프레임 사용");
            Add(t, "grid.size", "框占比", "Frame size", "フレーム比率", "프레임 비율");
            Add(t, "grid.density", "点间距", "Dot spacing", "ドット間隔", "도트 간격");
            Add(t, "grid.dotsize", "点大小", "Dot size", "ドットサイズ", "도트 크기");
            Add(t, "grid.opacity", "点阵透明度", "Dot opacity", "ドットの不透明度", "도트 불투명도");
            Add(t, "grid.lineover", "线与点不重叠（线优先）", "Hide dots under lines (lines on top)",
                "線とドットを重ねない（線を優先）", "선과 도트 겹치지 않기 (선 우선)");
            Add(t, "grid.hint",
                "与任何线段相交的圆点整个不再绘制。",
                "Dots intersecting any line are not drawn at all.",
                "いずれかの線と交わるドットは描画されません。",
                "어떤 선과 교차하는 도트는 그리지 않습니다.");
            Add(t, "grid.hint2",
                "框占比按屏幕比例缩放；点间距过大时框架会自动取整以保持比例。",
                "Frame scales with the screen aspect; spacing is rounded to keep the ratio exact.",
                "フレームは画面比率に追従します。間隔は比率を保つよう自動調整されます。",
                "프레임은 화면 비율을 따릅니다. 간격은 비율을 유지하도록 자동 조정됩니다.");
            Add(t, "color.main", "颜色（点与横线共用）", "Color (dots & line)",
                "色（ドットと線で共通）", "색상 (도트·선 공용)");
            Add(t, "color.palette", "主色与边框色", "Main & border colors",
                "メイン色と枠線色", "주 색상과 테두리 색");
            Add(t, "color.dot", "主色", "Main color", "メイン色", "주 색상");
            Add(t, "color.mainshared", "主色（点·线·X 共用）", "Main color (dots / lines / X)",
                "メイン色（共通）", "주 색상 (공용)");

            // ---------- 边框 ----------
            Add(t, "border.inner.enabled", "启用内边框", "Enable inner border",
                "内側の枠線を有効化", "안쪽 테두리 사용");
            Add(t, "border.inner.color", "内边框颜色", "Inner color", "内側の色", "안쪽 색상");
            Add(t, "border.inner.thickness", "内边框粗细", "Inner width", "内側の太さ", "안쪽 두께");
            Add(t, "border.inner.opacity", "内边框透明度", "Inner opacity", "内側の不透明度", "안쪽 불투명도");
            Add(t, "border.outer.enabled", "启用外边框", "Enable outer border",
                "外側の枠線を有効化", "바깥 테두리 사용");
            Add(t, "border.outer.color", "外边框颜色", "Outer color", "外側の色", "바깥 색상");
            Add(t, "border.outer.thickness", "外边框粗细", "Outer width", "外側の太さ", "바깥 두께");
            Add(t, "border.outer.opacity", "外边框透明度", "Outer opacity", "外側の不透明度", "바깥 불투명도");
            Add(t, "border.hint",
                "内边框贴本体，外边框套在内边框之外；点阵与横线共用。",
                "Inner sits against the shape, outer wraps around it. Shared by dots and line.",
                "内側は本体に沿い、外側はその外周を包みます。ドットと線で共通です。",
                "안쪽은 본체에 밀착되고 바깥쪽은 그 외곽을 감쌉니다. 도트와 선이 공유합니다.");

            // ---------- 中央横线 ----------
            Add(t, "center.enabled", "启用中央横线", "Enable center line",
                "中央ラインを有効化", "중앙 가로선 사용");
            Add(t, "center.gap", "中间留空", "Center gap", "中央の余白", "중앙 여백");
            Add(t, "center.offset", "垂直偏移", "Vertical offset", "縦方向のオフセット", "세로 오프셋");
            Add(t, "center.length", "横线长度", "Line length", "ラインの長さ", "선 길이");
            Add(t, "center.length.full", "整屏", "Full", "全画面", "전체 화면");
            Add(t, "center.thickness", "线条粗细", "Line thickness", "ラインの太さ", "선 두께");
            Add(t, "center.radius", "线条圆角", "Line rounding", "ラインの角丸", "선 모서리 둥글기");
            Add(t, "center.opacity", "线条透明度", "Line opacity", "ラインの不透明度", "선 불투명도");
            Add(t, "center.hint",
                "留空以像素为单位，不随分辨率变化；准星区域不会被遮挡。双击数值可直接输入。",
                "Gap is in pixels and independent of resolution. Double-click a value to type it.",
                "余白はピクセル単位で解像度に依存しません。准星（クロスヘア）領域は遮られません。数値をダブルクリックで直接入力できます。",
                "여백은 픽셀 단위이며 해상도와 무관합니다. 조준선 영역은 가려지지 않습니다. 값을 더블클릭하면 직접 입력할 수 있습니다.");

            // ---------- X Mode ----------
            Add(t, "x.enabled", "启用 X Mode", "Enable X Mode",
                "X モードを有効化", "X 모드 사용");
            Add(t, "x.offsetx", "交叉点水平偏移", "Crossing X offset", "交点の横オフセット", "교차점 가로 오프셋");
            Add(t, "x.offsety", "交叉点垂直偏移", "Crossing Y offset", "交点の縦オフセット", "교차점 세로 오프셋");
            Add(t, "x.gap", "中间留空", "Center gap", "中央の余白", "중앙 여백");
            Add(t, "x.length", "对角线长度", "Diagonal length", "対角線の長さ", "대각선 길이");
            Add(t, "x.thickness", "线条粗细", "Line thickness", "ラインの太さ", "선 두께");
            Add(t, "x.radius", "线条圆角", "Line rounding", "ラインの角丸", "선 모서리 둥글기");
            Add(t, "x.opacity", "线条透明度", "Line opacity", "ラインの不透明度", "선 불투명도");
            Add(t, "x.hint",
                "两条 45° 对角线在中心交叉并留空；参数含义与中央横线一致。长度为整屏时自动铺满。",
                "Two 45° diagonals cross at the center with a gap. Parameters mirror the center line.",
                "2 本の 45° 対角線が中央で交差し、中央は空きます。パラメータは中央ラインと同義です。",
                "두 개의 45° 대각선이 중앙에서 교차하며 가운데가 비어 있습니다. 매개변수는 중앙선과 같습니다.");

            // ---------- 屏幕 ----------
            Add(t, "screens.refresh", "刷新", "Refresh", "更新", "새로 고침");
            Add(t, "screens.refresh.hint", "切换 4:3 或改 DPI 后点刷新", "Click Refresh after switching to 4:3 or changing DPI",
                "4:3 に切り替えたり DPI を変えた後に更新を押してください", "4:3 전환 또는 DPI 변경 후 새로 고침을 누르세요");
            Add(t, "screens.refresh.done", "已重新读取当前的屏幕分辨率与 DPI", "Re-read current resolutions and DPI",
                "現在の解像度と DPI を読み直しました", "현재 해상도와 DPI를 다시 읽었습니다");
            Add(t, "screen.primary", "主", "Primary", "メイン", "주 화면");
            Add(t, "screen.dpi", "DPI", "DPI", "DPI", "DPI");

            // ---------- 系统 ----------
            Add(t, "sys.opacity", "全局透明度", "Global opacity", "全体の不透明度", "전체 불투명도");
            Add(t, "sys.autostart", "开机自动启动", "Start with Windows",
                "Windows 起動時に自動起動", "Windows 시작 시 자동 실행");
            Add(t, "sys.dpi", "跟随各屏 DPI 缩放", "Scale with per-monitor DPI",
                "モニターごとの DPI に合わせて拡大縮小", "모니터별 DPI에 맞춰 크기 조정");
            Add(t, "sys.close", "关闭窗口时", "On close", "ウィンドウを閉じたとき", "창을 닫을 때");
            Add(t, "sys.close.ask", "每次询问", "Ask every time", "毎回確認する", "매번 확인");
            Add(t, "sys.close.tray", "最小化到托盘继续运行", "Minimize to tray",
                "トレイに最小化して継続", "트레이로 최소화하여 계속 실행");
            Add(t, "sys.close.exit", "直接退出程序", "Exit program", "プログラムを終了", "프로그램 종료");
            Add(t, "sys.language", "界面语言", "Language", "表示言語", "인터페이스 언어");
            Add(t, "sys.datadir", "配置文件位置:", "Config location:",
                "設定ファイルの場所:", "설정 파일 위치:");
            Add(t, "lang.chinese", "简体中文", "简体中文", "简体中文", "简体中文");
            Add(t, "lang.english", "English", "English", "English", "English");
            Add(t, "lang.japanese", "日本語", "日本語", "日本語", "日本語");
            Add(t, "lang.korean", "한국어", "한국어", "한국어", "한국어");

            // ---------- 按钮 ----------
            Add(t, "btn.hide", "隐藏设置", "Hide settings", "設定を隠す", "설정 숨기기");
            Add(t, "btn.pause", "暂停", "Pause", "一時停止", "일시정지");
            Add(t, "btn.resume", "继续", "Resume", "再開", "재개");
            Add(t, "btn.reset", "重置本方案", "Reset profile", "このプロファイルをリセット", "이 프로필 초기화");
            Add(t, "btn.exit", "退出程序", "Exit", "終了", "종료");
            Add(t, "footer.hint",
                "改动即时生效。双击数值可精确输入。",
                "Changes apply instantly. Double-click a value to type it.",
                "変更は即座に反映されます。数値をダブルクリックで正確に入力できます。",
                "변경 사항은 즉시 적용됩니다. 값을 더블클릭하면 정확히 입력할 수 있습니다.");

            // ---------- 通用 ----------
            Add(t, "ok", "确定", "OK", "OK", "확인");
            Add(t, "cancel", "取消", "Cancel", "キャンセル", "취소");

            // ---------- 托盘 ----------
            Add(t, "tray.settings", "设置…", "Settings…", "設定…", "설정…");
            Add(t, "tray.toggle", "暂停覆盖", "Pause overlay", "オーバーレイを一時停止", "오버레이 일시 중지");
            Add(t, "tray.toggle.resume", "恢复覆盖", "Resume overlay", "オーバーレイを再開", "오버레이 재개");
            Add(t, "tray.hide", "隐藏设置", "Hide settings", "設定を隠す", "설정 숨기기");
            Add(t, "tray.exit", "退出", "Exit", "終了", "종료");
            Add(t, "tray.paused", "（已暂停）", " (paused)", "（一時停止中）", " (일시 중지됨)");
            Add(t, "tray.tip", "AgoAnchor", "AgoAnchor", "AgoAnchor", "AgoAnchor");
            Add(t, "tray.balloon.title", "AgoAnchor", "AgoAnchor",
                "AgoAnchor", "AgoAnchor");
            Add(t, "tray.balloon.tray",
                "已最小化到托盘，覆盖层继续工作。双击图标可重新打开设置。",
                "Minimized to tray. Overlay keeps running. Double-click the tray icon to reopen settings.",
                "トレイに最小化しました。オーバーレイは動作を続けます。アイコンをダブルクリックすると設定が開きます。",
                "트레이로 최소화했습니다. 오버레이는 계속 실행됩니다. 아이콘을 더블클릭하면 설정이 열립니다.");
            Add(t, "tray.profile", "方案", "Profile", "プロファイル", "프로필");

            // ---------- 关闭对话框 ----------
            Add(t, "close.title", "关闭 AgoAnchor", "Close AgoAnchor",
                "AgoAnchor を閉じる", "AgoAnchor 닫기");
            Add(t, "close.text",
                "要最小化到托盘继续运行，还是直接退出程序？",
                "Minimize to the tray and keep running, or exit completely?",
                "トレイに最小化して続行しますか、それとも終了しますか？",
                "트레이로 최소화하여 계속할까요, 아니면 완전히 종료할까요?");
            Add(t, "close.tray", "最小化到托盘", "Minimize to tray", "トレイに最小化", "트레이로 최소화");
            Add(t, "close.exit", "直接退出", "Exit", "終了する", "종료");
            Add(t, "close.cancel", "取消", "Cancel", "キャンセル", "취소");
            Add(t, "close.remember", "记住我的选择", "Remember my choice",
                "この選択を記憶する", "이 선택 기억하기");

            // ---------- 取色器 ----------
            Add(t, "color.title", "选择颜色", "Choose color", "色を選択", "색상 선택");
            Add(t, "color.system", "系统取色器…", "System picker…",
                "システムのカラーピッカー…", "시스템 색상 선택…");
            Add(t, "color.ok", "确定", "OK", "OK", "확인");
            Add(t, "color.cancel", "取消", "Cancel", "キャンセル", "취소");

            return t;
        }
    }
}








