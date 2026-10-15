using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodecWpf
{
    public partial class MainWindow : Window
    {
        private static readonly Brush CyanBrush =
            new SolidColorBrush(Color.FromRgb(0x22, 0xE9, 0xE9));
        private static readonly Brush ErrorBrush =
            new SolidColorBrush(Color.FromRgb(0xF0, 0x3C, 0xB4));

        public MainWindow()
        {
            InitializeComponent();

            foreach (var m in Codec.Methods)
                MethodBox.Items.Add(m);
            MethodBox.SelectedIndex = 0;   // base64

            foreach (var f in Codec.HexFormats)
                HexFormatBox.Items.Add(f);
            HexFormatBox.SelectedIndex = 0;

            UpdateButtons(false);   // 출력이 비어 있으므로 시작 시 비활성
        }

        private bool IsEncode => EncodeMode.IsChecked == true;
        private string CurrentMethod => MethodBox.SelectedItem as string ?? "base64";

        // AGAIN으로 몇 번째 변환인지 (사용자가 직접 입력하면 0으로 리셋)
        private int _chainDepth;
        private bool _chaining;

        // 파일을 끌어다 놓으면 파일 해시 모드 (입력을 직접 고치면 해제)
        private bool _fileMode;
        private bool _suppressInput;
        private int _hashJob;   // 연속으로 파일을 놓았을 때 늦게 끝난 이전 결과 무시용

        // 바이트 기반 방식에서 쓸 문자셋 (UTF-8 / EUC-KR)
        private string _charset = Codec.Utf8;

        private int GetShift()
        {
            return int.TryParse(ShiftBox.Text.Trim(), out int s) ? s : 3;
        }

        // 방식 바뀌면: shift 칸 표시 여부 + 중앙 라벨 갱신 후 재변환
        private void Method_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (ShiftPanel == null) return;   // 초기화 중 호출 방지

            UpdateOptionPanels();

            // auto는 디코딩 전용이므로 선택하면 DECODE로 전환 (Checked 이벤트가 Convert 호출)
            if (CurrentMethod == Codec.AutoMethod && IsEncode)
            {
                DecodeMode.IsChecked = true;
                return;
            }

            // 해시는 인코딩 전용이므로 선택하면 ENCODE로 전환
            if (Codec.IsHash(CurrentMethod) && !IsEncode)
            {
                EncodeMode.IsChecked = true;
                return;
            }

            Convert();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            UpdateOptionPanels();
            Convert();
        }

        private void HexFormat_Changed(object sender, SelectionChangedEventArgs e) => Convert();

        private void Charset_Click(object sender, RoutedEventArgs e)
        {
            _charset = _charset == Codec.Utf8 ? Codec.EucKr : Codec.Utf8;
            CharsetBtn.Content = _charset;
            Convert();
            UpdateInputStats();
            UpdateOutputStats();
        }

        // 방식/모드에 따라 보조 옵션 표시: caesar → shift, hex 인코딩 → 출력 형식
        private void UpdateOptionPanels()
        {
            if (ShiftPanel == null || HexPanel == null || EncodeMode == null) return;
            ShiftPanel.Visibility = CurrentMethod == "caesar"
                ? Visibility.Visible : Visibility.Collapsed;
            HexPanel.Visibility = CurrentMethod == "hex" && IsEncode
                ? Visibility.Visible : Visibility.Collapsed;
            // 한 방향 전용 방식은 반대 모드 버튼을 비활성화 (auto=디코딩 전용, 해시=인코딩 전용)
            EncodeMode.IsEnabled = CurrentMethod != Codec.AutoMethod;
            if (DecodeMode != null) DecodeMode.IsEnabled = !Codec.IsHash(CurrentMethod);
            if (CharsetBtn != null)
                CharsetBtn.IsEnabled = Codec.UsesCharset(CurrentMethod);   // rot13/caesar/morse/html엔 무관
        }

        private int HexFormat => HexFormatBox?.SelectedIndex >= 0 ? HexFormatBox.SelectedIndex : 0;
        private void Input_Changed(object sender, EventArgs e)
        {
            if (sender == InputBox && !_suppressInput)
            {
                if (!_chaining) _chainDepth = 0;   // 직접 입력 → 체인 리셋
                if (_fileMode)
                {
                    _fileMode = false;             // 직접 입력 → 파일 모드 해제
                    if (FindBar?.Visibility == Visibility.Visible) UpdateReplaceEnabled();
                }
            }
            if (sender == InputBox)
            {
                UpdateInputStats();
                if (FindBar?.Visibility == Visibility.Visible && _findTarget == InputBox)
                    RefreshMatches(select: false);   // 개수만 갱신, 커서는 건드리지 않음
            }
            if (_suppressInput) return;   // 파일 경로를 코드에서 넣는 중
            Convert();
        }

        // 실시간 변환 핵심
        private void Convert()
        {
            if (OutputBox == null || InputBox == null) return;
            if (_fileMode) return;   // 파일 해시 결과는 방식/모드를 바꿔도 유지

            // placeholder 힌트 표시/숨김
            if (InputHint != null)
                InputHint.Visibility = string.IsNullOrEmpty(InputBox.Text)
                    ? Visibility.Visible : Visibility.Collapsed;

            // 중앙 방향 라벨 갱신
            if (ArrowText != null)
            {
                string arrow = IsEncode ? "▼  {0} ▼" : "▲  {0} ▲";
                ArrowText.Text = string.Format(arrow, CurrentMethod.ToUpper() + "  ");
            }

            string input = InputBox.Text;
            if (string.IsNullOrEmpty(input))
            {
                OutputBox.Text = "";
                SetStatus("ready", false);
                UpdateButtons(false);
                return;
            }

            try
            {
                string result;
                string detail = "";

                if (!IsEncode && CurrentMethod == Codec.AutoMethod)
                {
                    // 자동 다중 디코딩: 거친 단계를 상태바에 표시
                    result = Codec.AutoDecode(input, out List<string> steps, _charset);
                    detail = $" · {string.Join(" → ", steps)} ({steps.Count} layer{(steps.Count > 1 ? "s" : "")})";
                }
                else
                {
                    result = IsEncode
                        ? Codec.Encode(CurrentMethod, input, GetShift(), HexFormat, _charset)
                        : Codec.Decode(CurrentMethod, input, GetShift(), _charset);
                }

                if (_chainDepth > 0) detail += $" · chain #{_chainDepth + 1}";

                bool changed = OutputBox.Text != result;
                OutputBox.Foreground = CyanBrush;
                OutputBox.Text = result;
                SetStatus($"{(IsEncode ? "encoded" : "decoded")}{detail} · {result.Length} chars", false);
                UpdateButtons(!string.IsNullOrEmpty(result));

                if (changed && !string.IsNullOrEmpty(result))
                    PulseOutputGlow();
            }
            catch (Exception ex)
            {
                OutputBox.Foreground = ErrorBrush;
                OutputBox.Text = "✗ " + ex.Message;
                SetStatus("입력 형식을 확인하세요", true);
                UpdateButtons(false);   // 에러 결과는 복사/스왑 대상 아님
            }
        }

        // 변환 성공 시 출력 패널이 잠깐 시안으로 빛났다 사라진다.
        private void PulseOutputGlow()
        {
            if (OutGlow == null || OutBorderBrush == null) return;

            // 바깥 글로우: 0 → 0.55 → 0
            var glow = new DoubleAnimationUsingKeyFrames();
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0.55, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))));
            glow.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.7))));
            OutGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, glow);

            // 테두리: 기본색 → 시안 → 기본색
            var border = new ColorAnimationUsingKeyFrames();
            border.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(0x2A, 0x25, 0x40), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            border.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(0x22, 0xE9, 0xE9), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.12))));
            border.KeyFrames.Add(new LinearColorKeyFrame(Color.FromRgb(0x2A, 0x25, 0x40), KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.7))));
            OutBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, border);
        }

        // 출력이 유효할 때만 COPY / SWAP 활성화
        private void UpdateButtons(bool hasValidOutput)
        {
            if (CopyBtn != null) CopyBtn.IsEnabled = hasValidOutput;
            if (AgainBtn != null) AgainBtn.IsEnabled = hasValidOutput;
            if (SwapBtn != null) SwapBtn.IsEnabled = hasValidOutput;
        }

        private void SetStatus(string msg, bool error)
        {
            if (StatusText == null) return;
            StatusText.Text = msg;
            StatusText.Foreground = error ? ErrorBrush
                : new SolidColorBrush(Color.FromRgb(0x6C, 0x6A, 0x86));
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(OutputBox.Text))
            {
                Clipboard.SetText(OutputBox.Text);
                SetStatus("클립보드에 복사됨 ✓", false);
            }
        }

        // 출력 → 입력으로 보내고 모드는 그대로 유지한다. (연속 디코딩/인코딩)
        // 예: base64를 두 번 감싼 문자열 → AGAIN 한 번 더 누르면 원문.
        //     중간에 방식을 바꿔 url → base64 같은 조합도 한 겹씩 벗길 수 있다.
        private void Again_Click(object sender, RoutedEventArgs e)
        {
            string output = OutputBox.Text;
            if (string.IsNullOrEmpty(output) || output.StartsWith("✗"))
                return;

            _chainDepth++;
            _chaining = true;
            InputBox.Text = output;                 // TextChanged → Convert 자동 실행
            _chaining = false;
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
        }

        // ── 파일 드래그 앤 드롭 → 파일 해시 ─────────────
        // TextBox는 기본적으로 파일 드롭을 막으므로 Preview 이벤트에서 직접 처리한다.
        private void Input_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async void Input_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Handled = true;

            var files = ((string[])e.Data.GetData(DataFormats.FileDrop))
                .Where(File.Exists).ToArray();
            if (files.Length == 0)
            {
                SetStatus("폴더는 해시할 수 없어요. 파일을 놓아주세요", true);
                return;
            }

            _suppressInput = true;
            InputBox.Text = string.Join(Environment.NewLine, files);
            _suppressInput = false;
            _fileMode = true;
            _chainDepth = 0;
            UpdateInputStats();   // 파일 모드에선 입력 통계 숨김
            if (FindBar.Visibility == Visibility.Visible) UpdateReplaceEnabled();
            if (InputHint != null) InputHint.Visibility = Visibility.Collapsed;

            int job = ++_hashJob;
            OutputBox.Foreground = CyanBrush;
            OutputBox.Text = "hashing...";
            SetStatus($"파일 해시 계산 중 · {files.Length} file(s)", false);
            UpdateButtons(false);

            try
            {
                // 큰 파일도 화면이 멈추지 않도록 백그라운드에서 계산
                string report = await Task.Run(() =>
                    string.Join(Environment.NewLine + Environment.NewLine,
                                files.Select(Codec.HashFileReport)));
                if (job != _hashJob || !_fileMode) return;   // 그 사이 다른 파일을 놓았거나 입력을 고침

                OutputBox.Text = report;
                SetStatus($"file hash · {files.Length} file(s) · 입력을 고치면 일반 모드로 돌아갑니다", false);
                CopyBtn.IsEnabled = true;     // 복사만 허용 (AGAIN/스왑은 파일 결과에 의미 없음)
                PulseOutputGlow();
            }
            catch (Exception ex)
            {
                if (job != _hashJob) return;
                OutputBox.Foreground = ErrorBrush;
                OutputBox.Text = "✗ " + ex.Message;
                SetStatus("파일을 읽을 수 없어요", true);
            }
        }

        // ── 줄 · 글자 · 바이트 표시 ─────────────────────
        private void Input_SelectionChanged(object sender, RoutedEventArgs e) => UpdateInputStats();
        private void Output_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateOutputStats();
            if (FindBar?.Visibility == Visibility.Visible && _findTarget == OutputBox)
                RefreshMatches(select: false);
        }

        private string BytesLabel(string text)
        {
            int b = Codec.ByteCount(text, _charset);
            return b < 0 ? $"{_charset} 불가" : $"{b:N0} B";
        }

        private void UpdateInputStats()
        {
            if (InputStats == null) return;
            string t = InputBox.Text;
            if (t.Length == 0 || _fileMode) { InputStats.Text = ""; return; }
            var (line, col) = Tools.LineCol(t, InputBox.CaretIndex);
            InputStats.Text = $"Ln {line}, Col {col} · {Tools.LineCount(t)}줄 · " +
                              $"{Tools.CharCount(t):N0}자 · {BytesLabel(t)}";
        }

        private void UpdateOutputStats()
        {
            if (OutputStats == null) return;
            string t = OutputBox.Text;
            // 비었거나 에러/계산 중 메시지면 숨김
            if (t.Length == 0 || t.StartsWith("✗") || t == "hashing...") { OutputStats.Text = ""; return; }
            OutputStats.Text = $"{Tools.LineCount(t)}줄 · {Tools.CharCount(t):N0}자 · {BytesLabel(t)}";
        }

        // ── 찾기 / 바꾸기 ───────────────────────────────
        // 마지막으로 포커스를 받은 창(입력/출력)에서 찾는다. 바꾸기는 입력창에서만.
        private TextBox _findTarget;
        private bool _caseSensitive;
        private List<int> _matches = new List<int>();
        private int _matchIdx = -1;

        private void Box_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb && tb != _findTarget)
            {
                _findTarget = tb;
                if (FindBar.Visibility == Visibility.Visible)
                {
                    UpdateReplaceEnabled();
                    RefreshMatches(select: false);
                }
            }
        }

        private void OpenFind(bool replace)
        {
            _findTarget ??= InputBox;
            FindBar.Visibility = Visibility.Visible;
            UpdateReplaceEnabled();

            // 한 줄짜리 선택 영역이 있으면 검색어로 채움
            string sel = _findTarget.SelectedText;
            if (!string.IsNullOrEmpty(sel) && sel.Length <= 200 && !sel.Contains('\n'))
                FindBox.Text = sel;

            if (replace && _findTarget == InputBox)
            {
                ReplaceBox.Focus();
                ReplaceBox.SelectAll();
            }
            else
            {
                FindBox.Focus();
                FindBox.SelectAll();
            }
            RefreshMatches(select: true);
        }

        private void CloseFind()
        {
            FindBar.Visibility = Visibility.Collapsed;
            (_findTarget ?? InputBox).Focus();
        }

        private void UpdateReplaceEnabled()
        {
            bool canReplace = _findTarget == InputBox && !_fileMode;
            ReplaceBox.IsEnabled = ReplaceBtn.IsEnabled = ReplaceAllBtn.IsEnabled = canReplace;
            ReplaceRow.ToolTip = canReplace ? null : "바꾸기는 입력창에서만 됩니다";
        }

        // select=true면 커서 이후 첫 일치로 이동해 선택, false면 개수만 다시 셈
        private void RefreshMatches(bool select)
        {
            var target = _findTarget ?? InputBox;
            _matches = Tools.FindAll(target.Text, FindBox.Text, _caseSensitive);
            if (_matches.Count == 0)
            {
                _matchIdx = -1;
            }
            else if (select)
            {
                int from = target.SelectionStart;
                _matchIdx = _matches.FindIndex(m => m >= from);
                if (_matchIdx < 0) _matchIdx = 0;
                SelectMatch();
            }
            else
            {
                // 현재 선택과 같은 위치의 일치가 있으면 번호 유지
                _matchIdx = _matches.IndexOf(target.SelectionStart);
            }
            UpdateMatchText();
        }

        private void SelectMatch()
        {
            if (_matchIdx < 0 || _matchIdx >= _matches.Count) return;
            var target = _findTarget ?? InputBox;
            int pos = _matches[_matchIdx];
            target.Select(pos, FindBox.Text.Length);
            int line = target.GetLineIndexFromCharacterIndex(pos);
            if (line >= 0) target.ScrollToLine(line);
            UpdateMatchText();
        }

        private void UpdateMatchText()
        {
            if (string.IsNullOrEmpty(FindBox.Text)) { MatchText.Text = ""; return; }
            MatchText.Text = _matches.Count == 0 ? "없음"
                : $"{(_matchIdx >= 0 ? _matchIdx + 1 : 0)}/{_matches.Count}";
            MatchText.Foreground = _matches.Count == 0 ? ErrorBrush
                : new SolidColorBrush(Color.FromRgb(0x8A, 0x86, 0xB8));
        }

        private void Step(int dir)
        {
            if (_matches.Count == 0) { RefreshMatches(select: true); return; }
            _matchIdx = ((_matchIdx < 0 ? (dir > 0 ? -1 : 0) : _matchIdx) + dir + _matches.Count) % _matches.Count;
            SelectMatch();
        }

        private void FindBox_Changed(object sender, TextChangedEventArgs e) => RefreshMatches(select: true);
        private void FindNext_Click(object sender, RoutedEventArgs e) => Step(+1);
        private void FindPrev_Click(object sender, RoutedEventArgs e) => Step(-1);
        private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();

        private void FindBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Step(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : +1);
            e.Handled = true;
        }

        private void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            Replace_Click(sender, e);
            e.Handled = true;
        }

        private void Case_Click(object sender, RoutedEventArgs e)
        {
            _caseSensitive = !_caseSensitive;
            CaseBtn.Opacity = _caseSensitive ? 1.0 : 0.45;
            RefreshMatches(select: true);
        }

        // 현재 일치 하나를 바꾸고 다음 일치로 이동
        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            if (_findTarget != InputBox || _fileMode || string.IsNullOrEmpty(FindBox.Text)) return;
            if (_matchIdx < 0) { RefreshMatches(select: true); if (_matchIdx < 0) return; }

            int pos = _matches[_matchIdx];
            string repl = ReplaceBox.Text;
            InputBox.Select(pos, FindBox.Text.Length);
            InputBox.SelectedText = repl;            // TextChanged → 실시간 변환도 같이 됨

            _matches = Tools.FindAll(InputBox.Text, FindBox.Text, _caseSensitive);
            if (_matches.Count == 0) { _matchIdx = -1; UpdateMatchText(); return; }
            _matchIdx = _matches.FindIndex(m => m >= pos + repl.Length);
            if (_matchIdx < 0) _matchIdx = 0;
            SelectMatch();
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            if (_findTarget != InputBox || _fileMode || string.IsNullOrEmpty(FindBox.Text)) return;
            string result = Tools.ReplaceAll(InputBox.Text, FindBox.Text, ReplaceBox.Text,
                                             _caseSensitive, out int count);
            if (count == 0) { SetStatus("바꿀 내용이 없어요", true); return; }
            InputBox.Text = result;
            RefreshMatches(select: false);
            SetStatus($"{count}곳을 바꿨어요", false);
        }

        // ── 도구 창 (하나만 열고, 이미 열려 있으면 앞으로 가져온다) ──
        private ToolsWindow _tools;

        private void Tools_Click(object sender, RoutedEventArgs e)
        {
            if (_tools == null || !_tools.IsLoaded)
            {
                _tools = new ToolsWindow { Owner = this };
                _tools.Show();
            }
            else
            {
                if (_tools.WindowState == WindowState.Minimized) _tools.WindowState = WindowState.Normal;
                _tools.Activate();
            }
        }

        // Ctrl+Enter = AGAIN
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.F || e.Key == Key.H))
            {
                OpenFind(replace: e.Key == Key.H);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
            {
                CloseFind();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (AgainBtn.IsEnabled) Again_Click(AgainBtn, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // 출력 → 입력으로 보내고 인코딩/디코딩 모드를 뒤집는다.
        // (예: base64 인코딩 결과를 그대로 디코딩 입력으로 확인)
        private void Swap_Click(object sender, RoutedEventArgs e)
        {
            string output = OutputBox.Text;
            if (string.IsNullOrEmpty(output) || output.StartsWith("✗"))
                return;

            // auto(디코딩 전용)와 해시(인코딩 전용)는 뒤집을 반대 모드가 없다
            if (CurrentMethod == Codec.AutoMethod || Codec.IsHash(CurrentMethod))
            {
                SetStatus($"{CurrentMethod}는 한 방향 전용이라 스왑할 수 없어요", true);
                return;
            }

            // 모드 뒤집기 (Checked 이벤트가 Convert를 부르므로 입력은 그 뒤에 넣는다)
            if (IsEncode) DecodeMode.IsChecked = true;
            else EncodeMode.IsChecked = true;

            _chainDepth = 0;
            InputBox.Text = output;                 // TextChanged → Convert 자동 실행
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
            SetStatus("입력 ↔ 출력 전환됨", false);
        }
    }
}
