using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MyGomiPinball
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    WriteCrashLog(e.Exception);
                    MessageBox.Show(e.Exception.Message, "마이곰이 종겜핀볼 실행 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    WriteCrashLog(e.ExceptionObject as Exception);
                };
                var mainForm = new MainForm();
                Application.Run(mainForm);
            }
            catch (Exception ex)
            {
                WriteCrashLog(ex);
                MessageBox.Show(ex.Message, "마이곰이 종겜핀볼 실행 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void WriteCrashLog(Exception ex)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(baseDir, "MyGomiPinball-error.log");
                string message = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine;
                message += ex == null ? "Unknown error" : ex.ToString();
                message += Environment.NewLine + Environment.NewLine;
                File.AppendAllText(path, message, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    internal static class UiFont
    {
        private static readonly string FamilyName = ResolveFamily();

        public static Font Make(float size, FontStyle style)
        {
            float snappedSize = Math.Max(7f, (float)Math.Round(size * 2f, MidpointRounding.AwayFromZero) / 2f);
            return new Font(FamilyName, snappedSize, style, GraphicsUnit.Point);
        }

        private static string ResolveFamily()
        {
            string[] preferred = new string[]
            {
                "Pretendard",
                "Noto Sans KR",
                "Malgun Gothic",
                "Segoe UI Variable Text",
                "Segoe UI"
            };

            try
            {
                foreach (FontFamily family in FontFamily.Families)
                {
                    for (int i = 0; i < preferred.Length; i++)
                    {
                        if (String.Equals(family.Name, preferred[i], StringComparison.OrdinalIgnoreCase))
                        {
                            return family.Name;
                        }
                    }
                }
            }
            catch
            {
            }

            return "Malgun Gothic";
        }
    }

    internal sealed class MainForm : Form
    {
        private const string PinballUrl = "https://gyeon-ai.github.io/MyGomiPinball-Web/";
        private const int DirectPinballUrlLimit = 1800;

        private readonly Color _text = Color.FromArgb(43, 28, 38);
        private readonly Color _muted = Color.FromArgb(92, 60, 72);
        private readonly Color _softMuted = Color.FromArgb(124, 83, 96);
        private readonly Color _purple = Color.FromArgb(229, 65, 101);
        private readonly Color _purpleDark = Color.FromArgb(136, 38, 65);
        private readonly Color _lavender = Color.FromArgb(255, 233, 238);
        private readonly Color _line = Color.FromArgb(241, 183, 195);
        private readonly Color _card = Color.FromArgb(255, 252, 250);
        private readonly Color _green = Color.FromArgb(51, 180, 151);
        private readonly Color _amber = Color.FromArgb(229, 151, 73);
        private readonly Color _red = Color.FromArgb(218, 83, 117);

        private GradientSurface _surface;
        private ToastBanner _toast;
        private FocusSinkPanel _focusSink;

        private ImageBadge _logo;
        private Label _appTitle;
        private Label _appSubtitle;
        private PillLabel _statusPill;

        private RoundedPanel _setupCard;
        private Label _streamLabel;
        private RoundTextBox _streamInput;
        private Label _conditionLabel;
        private RoundButton _exactButton;
        private RoundButton _atLeastButton;
        private Label _thresholdLabel;
        private NumberBox _thresholdInput;
        private RoundButton _targetButton;
        private GiftSourcePopup _targetPopup;
        private PopupDismissMessageFilter _targetPopupDismissFilter;
        private Label _sourceLabel;
        private RoundButton _nicknameSourceButton;
        private RoundButton _contentSourceButton;
        private RoundButton _connectButton;

        private RoundedPanel _collectionCard;
        private Label _collectionEyebrow;
        private Label _collectionTitle;
        private RoundTextBox _searchInput;
        private Label _searchCountLabel;
        private RoundButton _manualTestButton;
        private RoundButton _clearCollectionButton;
        private Panel _collectionDivider;
        private Panel _emptyState;
        private GradientBadge _emptyIcon;
        private Label _emptyTitle;
        private Label _emptyText;
        private VerticalScrollPanel _entryList;
        private readonly List<CollectedEntry> _visibleEntries = new List<CollectedEntry>();
        private string _visibleEntriesSearchText = "";
        private bool _layingOutEntryRows;
        private bool _scrollEntryListToBottomAfterRender;

        private RoundedPanel _pinballCard;
        private Label _pinballEyebrow;
        private Label _pinballTitle;
        private Label _pinballDesc;
        private Label _pinballInputLabel;
        private Label _pinballCountLabel;
        private RoundTextBox _pinballText;
        private RoundButton _openPinballButton;
        private RoundButton _copyButton;
        private RoundButton _saveButton;
        private readonly StringBuilder _generatedPinballText = new StringBuilder();
        private long _generatedPinballCoinTotal;
        private bool _pinballTextMatchesGenerated = true;
        private bool _updatingPinballText;

        private Label _footerLeft;
        private CreditBadge _footerRight;

        private readonly List<CollectedEntry> _entries = new List<CollectedEntry>();
        private readonly List<PendingGift> _pending = new List<PendingGift>();
        private readonly System.Windows.Forms.Timer _connectTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _reconnectTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _toastTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer _searchRefreshTimer = new System.Windows.Forms.Timer();

        private SoopLiveChatClient _chatClient;
        private string _streamerId = "";
        private string _streamerName = "";
        private bool _connected;
        private bool _connecting;
        private bool _manualDisconnect;
        private bool _exactMode;
        private bool _nicknamePinballMode;
        private bool _collectStarBalloon = true;
        private bool _collectAdBalloon = true;
        private bool _collectChallengeGift = true;
        private bool _capturingTargetPopupBackdrop;
        private int _reconnectAttempt;

        public MainForm()
        {
            Text = "마이곰이 종겜핀볼";
            ClientSize = new Size(1093, 688);
            MinimumSize = new Size(720, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiFont.Make(9.5f, FontStyle.Regular);
            BackColor = Color.FromArgb(255, 241, 243);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Icon = LoadEmbeddedIcon("MyGomiPinballIcon");

            BuildUi();
            WireEvents();
            int previewCount = GetArgumentInt("--preview-count=", 0, 1000);
            if (previewCount > 0)
            {
                SeedManyPreviewRows(previewCount);
            }
            else if (IsPreviewManyMode())
            {
                SeedManyPreviewRows();
            }
            else if (IsPreviewMode())
            {
                SeedPreviewRows();
            }
            string searchPreview = GetArgumentValue("--search-preview=");
            if (searchPreview.Length > 0)
            {
                _searchInput.Text = searchPreview;
            }
            RefreshModeButtons();
            RefreshSourceButtons();
            RefreshTargetButton();
            RefreshConnectionButton();
            RefreshStatus("● 연결 안 됨", _muted, Color.FromArgb(255, 246, 248));
            _searchRefreshTimer.Stop();
            RefreshCounts();

            Resize += delegate { LayoutUi(); };
            Shown += delegate
            {
                LayoutUi();
                int stressCount = GetArgumentInt("--stress-count=", 0, 1000);
                if (stressCount > 0)
                {
                    AddStressEntries(stressCount);
                    return;
                }

                string livePreviewId = GetArgumentValue("--live-preview=");
                if (livePreviewId.Length > 0)
                {
                    _streamInput.Text = livePreviewId;
                    StartConnection();
                    return;
                }

                if (IsManualTestPreviewMode())
                {
                    AddManualTestEntry();
                    return;
                }

                if (IsClearDialogPreviewMode())
                {
                    ClearEntries();
                    return;
                }

                if (IsTargetDialogPreviewMode())
                {
                    BeginInvoke((MethodInvoker)delegate { ShowTargetPopup(); });
                    return;
                }

                if (IsConnectingPreviewMode())
                {
                    string previewId = GetArgumentValue("--preview-soop-id=");
                    if (previewId.Length > 0)
                    {
                        _streamInput.Text = previewId;
                        _connectTimer.Interval = 7000;
                        StartConnection();
                    }
                    return;
                }

                if (IsCollectingPreviewMode())
                {
                    string previewId = GetArgumentValue("--preview-soop-id=");
                    if (previewId.Length > 0)
                    {
                        _streamInput.Text = previewId;
                        StartConnection();
                    }
                    return;
                }

                if (IsToastPreviewMode())
                {
                    ShowToast(GetStartToastText());
                }
            };
        }

        private void BuildUi()
        {
            _surface = new GradientSurface();
            _surface.Dock = DockStyle.Fill;
            _surface.AutoScroll = true;
            _surface.TopColor = Color.FromArgb(255, 248, 248);
            _surface.BottomColor = Color.FromArgb(255, 237, 241);
            Controls.Add(_surface);

            _focusSink = new FocusSinkPanel();
            _focusSink.SetBounds(0, 0, 1, 1);
            _surface.Controls.Add(_focusSink);
            _focusSink.SendToBack();

            _logo = new ImageBadge();
            _logo.Image = LoadEmbeddedImage("MyGomiPinballLogo");
            _logo.Radius = 12;
            _logo.FillColor = Color.Transparent;
            _logo.BorderColor = Color.Transparent;
            _surface.Controls.Add(_logo);

            _appTitle = PlainLabel("마이곰이 종겜핀볼", 17.0f, FontStyle.Bold, _text);
            _appTitle.ForeColor = _text;
            _appTitle.TextAlign = ContentAlignment.MiddleLeft;
            _surface.Controls.Add(_appTitle);

            _appSubtitle = PlainLabel("오늘 걸릴 종겜은?", 10.2f, FontStyle.Bold, _muted);
            _appSubtitle.ForeColor = Color.FromArgb(83, 48, 61);
            _appSubtitle.TextAlign = ContentAlignment.MiddleLeft;
            _surface.Controls.Add(_appSubtitle);

            _statusPill = new PillLabel();
            _statusPill.Font = UiFont.Make(9.0f, FontStyle.Bold);
            _statusPill.Radius = 16;
            _surface.Controls.Add(_statusPill);

            BuildSetupCard();
            BuildCollectionCard();
            BuildPinballCard();
            BuildTargetPopup();

            _footerLeft = PlainLabel("실시간 수집 • 자동 코인 계산 • 핀볼 사이트 연동", 8.0f, FontStyle.Bold, _softMuted);
            _footerLeft.ForeColor = _muted;
            _footerLeft.TextAlign = ContentAlignment.MiddleLeft;
            _surface.Controls.Add(_footerLeft);

            _footerRight = new CreditBadge();
            _footerRight.Text = "견아";
            _footerRight.Font = UiFont.Make(7.3f, FontStyle.Bold);
            _footerRight.ForeColor = _purpleDark;
            _footerRight.FillColor = Color.FromArgb(255, 230, 235);
            _footerRight.BorderColor = _line;
            _footerRight.MarkStartColor = Color.FromArgb(243, 96, 121);
            _footerRight.MarkEndColor = Color.FromArgb(255, 178, 137);
            _footerRight.Radius = 8;
            _surface.Controls.Add(_footerRight);

            _toast = new ToastBanner();
            _toast.Visible = false;
            _surface.Controls.Add(_toast);
            _toast.BringToFront();

            WireBlankFocusClearers();
            FormClosed += delegate
            {
                if (_targetPopupDismissFilter != null)
                {
                    Application.RemoveMessageFilter(_targetPopupDismissFilter);
                }
            };
        }

        private void WireBlankFocusClearers()
        {
            AttachClearFocusOnMouseDown(_surface);
            AttachClearFocusOnMouseDown(_setupCard);
            AttachClearFocusOnMouseDown(_collectionCard);
            AttachClearFocusOnMouseDown(_emptyState);
            AttachClearFocusOnMouseDown(_entryList);
            AttachClearFocusOnMouseDown(_pinballCard);
            AttachClearFocusOnMouseDown(_collectionEyebrow);
            AttachClearFocusOnMouseDown(_collectionTitle);
            AttachClearFocusOnMouseDown(_searchCountLabel);
            AttachClearFocusOnMouseDown(_pinballEyebrow);
            AttachClearFocusOnMouseDown(_pinballTitle);
            AttachClearFocusOnMouseDown(_pinballDesc);
            AttachClearFocusOnMouseDown(_pinballInputLabel);
            AttachClearFocusOnMouseDown(_footerLeft);
            AttachClearFocusOnMouseDown(_footerRight);
        }

        private void AttachClearFocusOnMouseDown(Control control)
        {
            if (control == null)
            {
                return;
            }

            control.MouseDown += delegate { ClearEditingFocus(); };
        }

        private void ClearEditingFocus()
        {
            if (_focusSink != null && _focusSink.CanFocus)
            {
                _focusSink.Focus();
                return;
            }

            ActiveControl = null;
        }

        private Image LoadEmbeddedImage(string resourceName)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            using (stream)
            {
                return new Bitmap(stream);
            }
        }

        private Icon LoadEmbeddedIcon(string resourceName)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            using (stream)
            using (var icon = new Icon(stream, 256, 256))
            {
                return (Icon)icon.Clone();
            }
        }

        private void BuildSetupCard()
        {
            _setupCard = Card();
            _surface.Controls.Add(_setupCard);

            _streamLabel = SmallHeader("방송 주소 또는 SOOP ID");
            _setupCard.Controls.Add(_streamLabel);

            _streamInput = new RoundTextBox(10.0f);
            _streamInput.Placeholder = "예: SOOP 방송 주소 또는 SOOP ID";
            _setupCard.Controls.Add(_streamInput);

            _conditionLabel = SmallHeader("수집 조건");
            _setupCard.Controls.Add(_conditionLabel);

            _exactButton = SegmentButton("정확히 N개");
            _setupCard.Controls.Add(_exactButton);

            _atLeastButton = SegmentButton("N개 이상");
            _setupCard.Controls.Add(_atLeastButton);

            _thresholdLabel = SmallHeader("기준 별풍선");
            _setupCard.Controls.Add(_thresholdLabel);

            _thresholdInput = new NumberBox();
            _thresholdInput.Value = 100;
            _thresholdInput.Minimum = 1;
            _thresholdInput.Maximum = 30000;
            _thresholdInput.SuffixText = "개";
            _setupCard.Controls.Add(_thresholdInput);

            _targetButton = SegmentButton("수집 대상 3/3 ▶");
            _targetButton.Click += delegate { ToggleTargetPopup(); };
            _setupCard.Controls.Add(_targetButton);

            _sourceLabel = SmallHeader("핀볼 반영");
            _setupCard.Controls.Add(_sourceLabel);

            _nicknameSourceButton = SegmentButton("닉네임으로");
            _setupCard.Controls.Add(_nicknameSourceButton);

            _contentSourceButton = SegmentButton("채팅내용으로");
            _setupCard.Controls.Add(_contentSourceButton);

            _connectButton = PrimaryButton("수집 시작");
            _connectButton.Font = UiFont.Make(11.0f, FontStyle.Bold);
            _setupCard.Controls.Add(_connectButton);
        }

        private void BuildTargetPopup()
        {
            _targetPopup = new GiftSourcePopup(
                _text,
                _muted,
                _purple,
                _line,
                _collectStarBalloon,
                _collectAdBalloon,
                _collectChallengeGift);
            _targetPopup.Visible = false;
            _targetPopup.SelectionChanged += delegate
            {
                _collectStarBalloon = _targetPopup.CollectStarBalloon;
                _collectAdBalloon = _targetPopup.CollectAdBalloon;
                _collectChallengeGift = _targetPopup.CollectChallengeGift;
                RefreshTargetButton();
                ShowToast("수집 대상이 변경됐습니다. 이후 수신 패킷부터 적용됩니다.");
            };
            _targetPopup.SelectionRejected += delegate
            {
                ShowToast("수집 대상은 최소 1개를 선택해야 합니다.");
            };
            Controls.Add(_targetPopup);
            _targetPopup.BringToFront();

            _targetPopupDismissFilter = new PopupDismissMessageFilter(
                _targetPopup,
                _targetButton,
                HideTargetPopup);
            Application.AddMessageFilter(_targetPopupDismissFilter);
        }

        private void BuildCollectionCard()
        {
            _collectionCard = Card();
            _surface.Controls.Add(_collectionCard);

            _collectionEyebrow = Eyebrow("LIVE COLLECTION");
            _collectionCard.Controls.Add(_collectionEyebrow);

            _collectionTitle = PlainLabel("수집된 핀볼 내용", 13.7f, FontStyle.Bold, _text);
            _collectionCard.Controls.Add(_collectionTitle);

            _searchInput = new RoundTextBox(10.0f);
            _searchInput.Placeholder = "닉네임, 내용, 후원 종류, 코인 검색";
            _searchRefreshTimer.Interval = 160;
            _searchRefreshTimer.Tick += delegate
            {
                _searchRefreshTimer.Stop();
                RefreshCounts();
            };
            _searchInput.InnerTextChanged += delegate
            {
                _searchRefreshTimer.Stop();
                _searchRefreshTimer.Start();
            };
            _collectionCard.Controls.Add(_searchInput);

            _searchCountLabel = PlainLabel("", 8.2f, FontStyle.Bold, _purple);
            _searchCountLabel.TextAlign = ContentAlignment.MiddleRight;
            _searchCountLabel.Visible = false;
            _collectionCard.Controls.Add(_searchCountLabel);

            _manualTestButton = HeaderActionButton("수동 테스트", true);
            _manualTestButton.Glyph = ButtonGlyph.Flask;
            _manualTestButton.Click += delegate { AddManualTestEntry(); };
            _collectionCard.Controls.Add(_manualTestButton);

            _clearCollectionButton = HeaderActionButton("모두 지우기", false);
            _clearCollectionButton.Glyph = ButtonGlyph.Trash;
            _clearCollectionButton.Click += delegate { ClearEntries(); };
            _collectionCard.Controls.Add(_clearCollectionButton);

            _collectionDivider = new Panel();
            _collectionDivider.BackColor = _line;
            _collectionCard.Controls.Add(_collectionDivider);

            _emptyState = new BufferedPanel();
            _emptyState.BackColor = _card;
            _collectionCard.Controls.Add(_emptyState);

            _emptyIcon = new GradientBadge();
            _emptyIcon.Text = "✦";
            _emptyIcon.Font = UiFont.Make(20f, FontStyle.Bold);
            _emptyIcon.ForeColor = _purple;
            _emptyIcon.StartColor = Color.FromArgb(255, 224, 230);
            _emptyIcon.EndColor = Color.FromArgb(255, 246, 222);
            _emptyState.Controls.Add(_emptyIcon);

            _emptyTitle = PlainLabel("아직 수집된 채팅이 없어요", 11.5f, FontStyle.Bold, _text);
            _emptyTitle.TextAlign = ContentAlignment.MiddleCenter;
            _emptyState.Controls.Add(_emptyTitle);

            _emptyText = PlainLabel("정해진 별풍선 조건을 보낸 시청자의\r\n다음 채팅 1회가 여기에 추가됩니다.", 10.0f, FontStyle.Regular, _muted);
            _emptyText.TextAlign = ContentAlignment.MiddleCenter;
            _emptyState.Controls.Add(_emptyText);

            _entryList = new VerticalScrollPanel();
            _entryList.Visible = false;
            _entryList.BackColor = _card;
            _entryList.Resize += delegate { GrowEntryRowPoolForViewport(); };
            _entryList.ScrollOffsetChanged += delegate { PositionEntryRows(); };
            _entryList.MouseDown += delegate { ClearEditingFocus(); };
            _collectionCard.Controls.Add(_entryList);
        }

        private void BuildPinballCard()
        {
            _pinballCard = Card();
            _surface.Controls.Add(_pinballCard);

            _pinballEyebrow = Eyebrow("PINBALL COIN");
            _pinballCard.Controls.Add(_pinballEyebrow);

            _pinballTitle = PlainLabel("핀볼은 여기로", 15.0f, FontStyle.Bold, _text);
            _pinballTitle.TextAlign = ContentAlignment.MiddleLeft;
            _pinballCard.Controls.Add(_pinballTitle);

            _pinballDesc = PlainLabel(BuildCoinGuideText(), 10.0f, FontStyle.Regular, Color.FromArgb(99, 65, 76));
            _pinballDesc.AutoEllipsis = false;
            _pinballCard.Controls.Add(_pinballDesc);

            _pinballInputLabel = PlainLabel("반영된 코인", 9.5f, FontStyle.Bold, _text);
            _pinballInputLabel.TextAlign = ContentAlignment.MiddleLeft;
            _pinballCard.Controls.Add(_pinballInputLabel);

            _pinballCountLabel = PlainLabel("0개", 9.5f, FontStyle.Bold, _purpleDark);
            _pinballCountLabel.TextAlign = ContentAlignment.MiddleRight;
            _pinballCard.Controls.Add(_pinballCountLabel);

            _pinballText = new RoundTextBox(10.0f);
            _pinballText.Multiline = true;
            _pinballText.Placeholder = "수집된 내용이 이곳에 표시됩니다";
            _pinballText.InnerTextChanged += delegate
            {
                if (!_updatingPinballText)
                {
                    _pinballTextMatchesGenerated = false;
                }
                RefreshPinballTotalLabel();
            };
            _pinballCard.Controls.Add(_pinballText);

            _openPinballButton = GradientButton("핀볼 사이트 열기  ↗");
            _openPinballButton.Font = UiFont.Make(11.4f, FontStyle.Bold);
            _openPinballButton.FillColor = Color.FromArgb(230, 70, 105);
            _openPinballButton.GradientColor = Color.FromArgb(250, 113, 122);
            _openPinballButton.BorderColor = Color.FromArgb(236, 138, 157);
            _pinballCard.Controls.Add(_openPinballButton);

            _copyButton = SecondaryButton("내용 복사");
            _copyButton.Font = UiFont.Make(9.5f, FontStyle.Bold);
            _pinballCard.Controls.Add(_copyButton);

            _saveButton = SecondaryButton("메모장 저장");
            _saveButton.Font = UiFont.Make(9.5f, FontStyle.Bold);
            _pinballCard.Controls.Add(_saveButton);

        }

        private void WireEvents()
        {
            _exactButton.Click += delegate
            {
                _exactMode = true;
                RefreshModeButtons();
                ShowToast(GetConditionToastText());
            };
            _atLeastButton.Click += delegate
            {
                _exactMode = false;
                RefreshModeButtons();
                ShowToast(GetConditionToastText());
            };
            _nicknameSourceButton.Click += delegate
            {
                if (!_nicknamePinballMode)
                {
                    _pending.Clear();
                }
                _nicknamePinballMode = true;
                RefreshSourceButtons();
                ShowToast("닉네임을 핀볼에 반영합니다.");
            };
            _contentSourceButton.Click += delegate
            {
                if (_nicknamePinballMode)
                {
                    _pending.Clear();
                }
                _nicknamePinballMode = false;
                RefreshSourceButtons();
                ShowToast("채팅 내용을 핀볼에 반영합니다.");
            };
            _thresholdInput.ValueChanged += delegate
            {
                RecalculateCoins();
                RefreshCoinGuide();
                RefreshPinballText(false);
                RefreshCounts();
            };
            _connectButton.Click += delegate
            {
                if (_connected || _connectTimer.Enabled || _reconnectTimer.Enabled)
                {
                    Disconnect(true);
                }
                else
                {
                    StartConnection();
                }
            };
            _openPinballButton.Click += delegate { OpenPinballSite(); };
            _copyButton.Click += delegate { CopyPinballText(); };
            _saveButton.Click += delegate { SavePinballText(); };

            _toastTimer.Interval = 2600;
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                if (_toast != null)
                {
                    _toast.Visible = false;
                }
            };

            _connectTimer.Interval = 800;
            _connectTimer.Tick += delegate
            {
                _connectTimer.Stop();
            };

            _reconnectTimer.Tick += delegate
            {
                _reconnectTimer.Stop();
                if (!_manualDisconnect && !_connected)
                {
                    StartConnection();
                }
            };
        }

        private void LayoutUi()
        {
            if (_surface == null)
            {
                return;
            }

            _surface.SuspendLayout();

            int viewportWidth = Math.Max(320, _surface.Width);
            int viewportHeight = Math.Max(320, _surface.ClientSize.Height);
            int margin = viewportWidth < 520 ? 8 : 18;
            int contentWidth = Math.Max(300, viewportWidth - (margin * 2));
            bool stackedLayout = contentWidth < 920;
            // 넓은 창도 높이가 부족하면 전체 내용을 스크롤할 수 있어야 한다.
            bool scrollLayout = stackedLayout || viewportHeight < 678;
            int previousSurfaceScrollY = scrollLayout ? Math.Max(0, -_surface.AutoScrollPosition.Y) : 0;
            _surface.AutoScrollPosition = Point.Empty;
            if (scrollLayout)
            {
                contentWidth = Math.Max(300, viewportWidth - (margin * 2) - SystemInformation.VerticalScrollBarWidth);
                _surface.AutoScroll = true;
            }
            else
            {
                _surface.AutoScrollPosition = Point.Empty;
                _surface.AutoScrollMinSize = Size.Empty;
                _surface.AutoScroll = false;
            }
            int y = viewportWidth < 520 ? 10 : 16;

            LayoutHeader(margin, y, contentWidth);
            LayoutToast(margin, y, contentWidth);
            y += viewportWidth < 520 ? 78 : 76;

            if (!stackedLayout)
            {
                int gap = 14;
                int leftWidth = Math.Min(350, Math.Max(300, contentWidth / 4));
                int rightWidth = contentWidth - leftWidth - gap;
                int footerReserve = 28;
                int pinballHeight = 160;
                int upperHeight = Math.Max(384, viewportHeight - y - pinballHeight - gap - footerReserve);

                LayoutSetupCard(margin, y, leftWidth, upperHeight);
                LayoutCollectionCard(margin + leftWidth + gap, y, rightWidth, upperHeight);
                y += upperHeight + gap;
                LayoutPinballCard(margin, y, contentWidth, pinballHeight);
                y += pinballHeight + 2;
            }
            else
            {
                int setupHeight = 390;
                int collectionHeight = contentWidth < 520 ? 420 : 430;
                // 입력 영역과 버튼 세 개 아래까지 필요한 높이와 여백을 확보한다.
                int pinballHeight = 382;
                LayoutSetupCard(margin, y, contentWidth, setupHeight);
                y += setupHeight + 12;
                LayoutCollectionCard(margin, y, contentWidth, collectionHeight);
                y += collectionHeight + 12;
                LayoutPinballCard(margin, y, contentWidth, pinballHeight);
                y += pinballHeight + 2;
            }

            LayoutFooter(margin, y, contentWidth);
            LayoutTargetPopup();
            y += stackedLayout ? 34 : 20;

            int scrollContentHeight = y + (stackedLayout ? 10 : 2);
            _surface.AutoScrollMinSize = scrollLayout ? new Size(0, scrollContentHeight) : Size.Empty;
            _surface.ResumeLayout();
            if (scrollLayout && previousSurfaceScrollY > 0)
            {
                int maxScrollY = Math.Max(0, scrollContentHeight - _surface.ClientSize.Height);
                int restoreScrollY = Math.Min(previousSurfaceScrollY, maxScrollY);
                if (restoreScrollY > 0)
                {
                    _surface.AutoScrollPosition = new Point(0, restoreScrollY);
                }
            }
            _surface.Invalidate();
        }

        private void LayoutToast(int x, int y, int width)
        {
            if (_toast == null)
            {
                return;
            }

            int measured = TextRenderer.MeasureText(_toast.Text, _toast.Font).Width + 92;
            int available = Math.Max(220, width - 48);
            int toastWidth = Math.Min(available, Math.Max(320, measured));
            int toastX = x + ((width - toastWidth) / 2);

            _toast.SetBounds(toastX, y + 5, toastWidth, 38);
            if (_toast.Visible)
            {
                _toast.BringToFront();
            }
        }

        private void LayoutHeader(int x, int y, int width)
        {
            int measuredStatus = TextRenderer.MeasureText(_statusPill.Text, _statusPill.Font).Width + 38;
            int maxStatus = width < 520 ? Math.Max(112, width - 300) : Math.Max(132, width - 430);
            int statusWidth = Math.Min(Math.Max(width < 460 ? 112 : 132, measuredStatus), maxStatus);
            int titleWidth = Math.Max(120, width - statusWidth - 88);

            _logo.SetBounds(x, y, 68, 68);
            _appTitle.SetBounds(x + 80, y + 6, titleWidth - 16, 34);
            _appSubtitle.SetBounds(x + 80, y + 40, titleWidth - 16, 22);
            _appSubtitle.Visible = width >= 420;

            _statusPill.SetBounds(x + width - statusWidth, y + 12, statusWidth, 34);
        }

        private void LayoutSetupCard(int x, int y, int width, int height)
        {
            _setupCard.SetBounds(x, y, width, height);
            int pad = width < 420 ? 16 : 20;
            int fieldW = width - (pad * 2);
            int halfW = (fieldW - 8) / 2;

            _streamLabel.SetBounds(pad, 18, fieldW, 20);
            _streamInput.SetBounds(pad, 42, fieldW, 36);
            _connectButton.SetBounds(pad, 88, fieldW, 44);

            const int sectionGap = 18;
            const int labelToControlGap = 6;
            int thresholdLabelY = _connectButton.Bottom + sectionGap;
            int thresholdControlsY = thresholdLabelY + 18 + labelToControlGap;
            _thresholdLabel.SetBounds(pad, thresholdLabelY, fieldW, 20);
            _thresholdInput.SetBounds(pad, thresholdControlsY, halfW, 36);
            _targetButton.SetBounds(pad + halfW + 8, thresholdControlsY, halfW, 36);

            int conditionLabelY = thresholdControlsY + 36 + sectionGap;
            int conditionControlsY = conditionLabelY + 18 + labelToControlGap;
            _conditionLabel.SetBounds(pad, conditionLabelY, fieldW, 20);
            _exactButton.SetBounds(pad, conditionControlsY, halfW, 36);
            _atLeastButton.SetBounds(pad + halfW + 8, conditionControlsY, halfW, 36);

            int sourceLabelY = conditionControlsY + 36 + sectionGap;
            int sourceControlsY = sourceLabelY + 18 + labelToControlGap;
            _sourceLabel.SetBounds(pad, sourceLabelY, fieldW, 20);
            _nicknameSourceButton.SetBounds(pad, sourceControlsY, halfW, 36);
            _contentSourceButton.SetBounds(pad + halfW + 8, sourceControlsY, halfW, 36);
        }

        private void LayoutTargetPopup()
        {
            if (_targetPopup == null || _targetButton == null || _setupCard == null)
            {
                return;
            }

            Point anchor = PointToClient(_targetButton.PointToScreen(Point.Empty));
            Point setupRight = PointToClient(_setupCard.PointToScreen(new Point(_setupCard.Width, 0)));
            int popupX = setupRight.X + 2;
            int popupY = anchor.Y;
            if (popupX + _targetPopup.Width > ClientSize.Width - 12)
            {
                popupX = Math.Max(12, anchor.X + _targetButton.Width - _targetPopup.Width);
                popupY = anchor.Y + _targetButton.Height + 6;
            }
            if (popupY + _targetPopup.Height > ClientSize.Height - 12)
            {
                popupY = Math.Max(12, anchor.Y - _targetPopup.Height - 6);
            }

            _targetPopup.Location = new Point(popupX, popupY);
            if (_targetPopup.Visible)
            {
                RefreshTargetPopupBackdrop();
                _targetPopup.BringToFront();
            }
        }

        private void RefreshTargetPopupBackdrop()
        {
            if (_capturingTargetPopupBackdrop || _targetPopup == null || _surface == null ||
                _surface.Width <= 0 || _surface.Height <= 0)
            {
                return;
            }

            _capturingTargetPopupBackdrop = true;
            try
            {
                using (var surfaceBitmap = new Bitmap(_surface.Width, _surface.Height, PixelFormat.Format32bppPArgb))
                using (var backdrop = new Bitmap(_targetPopup.Width, _targetPopup.Height, PixelFormat.Format32bppPArgb))
                {
                    _surface.DrawToBitmap(surfaceBitmap, _surface.ClientRectangle);
                    Point popupOnSurface = _surface.PointToClient(PointToScreen(_targetPopup.Location));
                    using (Graphics graphics = Graphics.FromImage(backdrop))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(
                            surfaceBitmap,
                            new Rectangle(Point.Empty, backdrop.Size),
                            new Rectangle(popupOnSurface, backdrop.Size),
                            GraphicsUnit.Pixel);
                    }
                    _targetPopup.SetBackdropImage(backdrop);
                }
            }
            finally
            {
                _capturingTargetPopupBackdrop = false;
            }
        }

        private void LayoutCollectionCard(int x, int y, int width, int height)
        {
            _collectionCard.SetBounds(x, y, width, height);
            int pad = width < 420 ? 16 : 24;

            _collectionEyebrow.SetBounds(pad, 20, 180, 18);

            if (width >= 720)
            {
                int actionsW = 224;
                int actionsX = width - pad - actionsW;
                int titleW = Math.Min(250, Math.Max(180, width / 4));
                int searchX = pad + titleW + 10;
                int searchW = Math.Max(190, actionsX - searchX - 14);
                int headerDividerY = 82;
                _collectionTitle.SetBounds(pad - 2, 40, titleW, 28);
                _searchInput.SetBounds(searchX, 28, searchW, 36);
                _searchCountLabel.SetBounds(searchX + searchW - 62, 66, 62, 16);
                _manualTestButton.SetBounds(actionsX, 28, 108, 36);
                _clearCollectionButton.SetBounds(actionsX + 116, 28, 108, 36);
                _collectionDivider.SetBounds(pad, headerDividerY, width - (pad * 2), 1);
            }
            else if (width >= 520)
            {
                int actionsX = width - pad - 234;
                int headerDividerY = 116;
                int actionY = 20;
                _collectionTitle.SetBounds(pad - 2, 42, Math.Max(180, actionsX - pad - 16), 28);
                _manualTestButton.SetBounds(actionsX, actionY, 112, 34);
                _clearCollectionButton.SetBounds(actionsX + 126, actionY, 108, 34);
                int searchY = 78;
                _searchInput.SetBounds(pad, searchY, Math.Max(180, width - (pad * 2)), 32);
                _collectionDivider.SetBounds(pad, headerDividerY, width - (pad * 2), 1);
            }
            else if (width >= 430)
            {
                int actionsX = width - pad - 234;
                _collectionTitle.SetBounds(pad - 2, 42, Math.Max(160, actionsX - pad - 12), 28);
                _manualTestButton.SetBounds(actionsX, 66, 112, 34);
                _clearCollectionButton.SetBounds(actionsX + 126, 66, 108, 34);
                int searchY = 112;
                _searchInput.SetBounds(pad, searchY, Math.Max(150, width - (pad * 2)), 32);
                _collectionDivider.SetBounds(pad, 150, width - (pad * 2), 1);
            }
            else
            {
                _collectionTitle.SetBounds(pad - 2, 42, width - (pad * 2) + 2, 28);
                _manualTestButton.SetBounds(pad, 78, 112, 34);
                _clearCollectionButton.SetBounds(pad + 126, 78, Math.Min(108, width - (pad * 2) - 126), 34);
                int searchY = 124;
                _searchInput.SetBounds(pad, searchY, width - (pad * 2), 32);
                _collectionDivider.SetBounds(pad, 164, width - (pad * 2), 1);
            }

            int dividerY = _collectionDivider.Top;
            int bodyY = dividerY + (_visibleEntries.Count > 0 ? 1 : 14);
            int availableBodyH = Math.Max(180, height - bodyY - 22);
            int listHeight = Math.Max(EntryRowControl.RowHeight, (availableBodyH / EntryRowControl.RowHeight) * EntryRowControl.RowHeight);
            _emptyState.SetBounds(pad, bodyY, width - (pad * 2), availableBodyH);
            _entryList.SetBounds(pad, bodyY, width - (pad * 2), listHeight);
            LayoutEmptyState();
            LayoutEntryRows();
        }

        private void LayoutEmptyState()
        {
            int w = _emptyState.Width;
            int h = _emptyState.Height;
            int centerX = w / 2;
            int groupHeight = 70 + 22 + 28 + 10 + 48;
            int startY = Math.Max(10, (h - groupHeight) / 2);
            int titleW = Math.Min(320, Math.Max(220, w - 24));
            int textW = Math.Min(360, Math.Max(240, w - 24));

            _emptyIcon.SetBounds(centerX - 35, startY, 70, 70);
            _emptyTitle.SetBounds(centerX - (titleW / 2), startY + 92, titleW, 28);
            _emptyText.SetBounds(centerX - (textW / 2), startY + 130, textW, 48);
        }

        private int GetRequiredEntryRowCount()
        {
            if (_entryList == null)
            {
                return 0;
            }

            int visibleRowCapacity = Math.Max(
                1,
                (_entryList.ClientSize.Height + EntryRowControl.RowHeight - 1) / EntryRowControl.RowHeight);
            return Math.Min(_visibleEntries.Count, visibleRowCapacity + 1);
        }

        private void GrowEntryRowPoolForViewport()
        {
            if (_entryList == null)
            {
                return;
            }

            int requiredRows = GetRequiredEntryRowCount();
            if (_entryList.Controls.Count >= requiredRows)
            {
                return;
            }

            _entryList.SuspendLayout();
            while (_entryList.Controls.Count < requiredRows)
            {
                AddEntryRowControl();
            }
            _entryList.ResumeLayout(false);
            LayoutEntryRows();
        }

        private void AddEntryRowControl()
        {
            int index = _entryList.Controls.Count;
            EntryRowControl row = new EntryRowControl(
                _visibleEntries[index],
                index + 1,
                _text,
                _muted,
                _purple,
                _lavender,
                _line,
                _card);
            row.EntryChanged += delegate { RefreshPinballText(false); };
            row.BlankClicked += delegate { ClearEditingFocus(); };
            AttachEntryScrollWheel(row);
            EntryRowControl rowForEvent = row;
            row.DeleteClicked += delegate
            {
                int entryIndex = _entries.IndexOf(rowForEvent.Entry);
                if (entryIndex >= 0)
                {
                    _entries.RemoveAt(entryIndex);
                    RefreshPinballText(false);
                    RefreshCounts();
                }
            };
            _entryList.Controls.Add(row);
        }

        private void RenderEntryRows()
        {
            if (_entryList == null)
            {
                return;
            }

            int requiredRows = GetRequiredEntryRowCount();

            _entryList.SuspendLayout();
            while (_entryList.Controls.Count > requiredRows)
            {
                Control last = _entryList.Controls[_entryList.Controls.Count - 1];
                _entryList.Controls.RemoveAt(_entryList.Controls.Count - 1);
                last.Dispose();
            }

            while (_entryList.Controls.Count < requiredRows)
            {
                AddEntryRowControl();
            }

            foreach (Control control in _entryList.Controls)
            {
                EntryRowControl row = control as EntryRowControl;
                if (row != null)
                {
                    row.RefreshEntryValues();
                }
            }

            _entryList.ResumeLayout(false);
            LayoutEntryRows();
        }

        private void LayoutEntryRows()
        {
            if (_entryList == null || _layingOutEntryRows)
            {
                return;
            }

            _layingOutEntryRows = true;
            _entryList.SuspendLayout();
            try
            {
                int totalHeight = _visibleEntries.Count * EntryRowControl.RowHeight;
                int previousScrollY = _entryList.ScrollOffset;
                _entryList.SetContentHeight(totalHeight);

                if (_scrollEntryListToBottomAfterRender)
                {
                    _entryList.ScrollToBottom();
                }
                else
                {
                    _entryList.ScrollTo(previousScrollY);
                }

                PositionEntryRows();
            }
            finally
            {
                bool scrollToBottom = _scrollEntryListToBottomAfterRender;
                _entryList.ResumeLayout();
                _scrollEntryListToBottomAfterRender = false;
                _layingOutEntryRows = false;
                if (scrollToBottom)
                {
                    ScrollEntryListToBottom();
                }
                _entryList.Invalidate();
            }
        }

        private void PositionEntryRows()
        {
            // 보이는 행만 재사용하며, 편집 중인 행은 다른 항목에 재배정하지 않는다.
            if (_entryList == null)
            {
                return;
            }

            int rowHeight = EntryRowControl.RowHeight;
            int offset = _entryList.ScrollOffset;
            int firstVisible = Math.Max(0, offset / rowHeight);
            int lastVisible = Math.Min(
                _visibleEntries.Count - 1,
                Math.Max(firstVisible, (offset + Math.Max(1, _entryList.ClientSize.Height) - 1) / rowHeight));
            int rowWidth = Math.Max(240, _entryList.ContentWidth - 2);
            var availableRows = new List<EntryRowControl>();
            foreach (Control control in _entryList.Controls)
            {
                EntryRowControl row = control as EntryRowControl;
                if (row != null)
                {
                    availableRows.Add(row);
                }
            }

            _entryList.SuspendLayout();
            try
            {
                for (int entryIndex = firstVisible; entryIndex <= lastVisible; entryIndex++)
                {
                    CollectedEntry entry = _visibleEntries[entryIndex];
                    EntryRowControl row = null;
                    for (int i = 0; i < availableRows.Count; i++)
                    {
                        if (Object.ReferenceEquals(availableRows[i].Entry, entry))
                        {
                            row = availableRows[i];
                            availableRows.RemoveAt(i);
                            break;
                        }
                    }

                    if (row == null && availableRows.Count > 0)
                    {
                        row = availableRows[0];
                        availableRows.RemoveAt(0);
                    }
                    if (row == null)
                    {
                        continue;
                    }

                    row.SetEntry(entry, entryIndex + 1);
                    Rectangle bounds = new Rectangle(0, (entryIndex * rowHeight) - offset, rowWidth, rowHeight);
                    if (row.Bounds != bounds)
                    {
                        row.Bounds = bounds;
                    }
                    if (!row.Visible)
                    {
                        row.Visible = true;
                    }
                }

                foreach (EntryRowControl row in availableRows)
                {
                    if (row.Visible)
                    {
                        row.Visible = false;
                    }
                }
            }
            finally
            {
                _entryList.ResumeLayout(false);
            }
        }

        private void ScrollEntryListToBottom()
        {
            if (_entryList == null)
            {
                return;
            }

            _entryList.ScrollToBottom();
        }

        private void AttachEntryScrollWheel(Control control)
        {
            if (control == null)
            {
                return;
            }

            control.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                ClearEditingFocus();
                _entryList.ScrollByWheel(e.Delta);
            };

            foreach (Control child in control.Controls)
            {
                AttachEntryScrollWheel(child);
            }
        }

        private void LayoutPinballCard(int x, int y, int width, int height)
        {
            _pinballCard.SetBounds(x, y, width, height);
            int pad = width < 420 ? 16 : 24;

            if (width >= 900)
            {
                int actionsW = Math.Min(286, Math.Max(250, width / 4));
                const int smallButtonGap = 10;
                if (((actionsW - smallButtonGap) & 1) != 0)
                {
                    actionsW--;
                }

                int centerX = (int)Math.Round(width * 0.20);
                int leftW = centerX - pad - 12;
                int centerW = Math.Max(280, width - centerX - actionsW - pad - 14);
                int actionsX = width - pad - actionsW;

                _pinballEyebrow.SetBounds(pad, 14, leftW, 20);
                _pinballTitle.SetBounds(pad - 2, 35, leftW + 2, 31);
                _pinballDesc.SetBounds(pad, 68, leftW, 44);
                _pinballInputLabel.SetBounds(centerX + 13, 14, centerW - 105, 20);
                _pinballCountLabel.SetBounds(centerX + centerW - 92, 14, 92, 20);
                _pinballText.SetBounds(centerX, 38, centerW, 108);
                _openPinballButton.SetBounds(actionsX, 14, actionsW, 66);
                int wideSmallW = (actionsW - smallButtonGap) / 2;
                int actionSmallY = 90;
                _copyButton.SetBounds(actionsX, actionSmallY, wideSmallW, 56);
                _saveButton.SetBounds(actionsX + wideSmallW + smallButtonGap, actionSmallY, wideSmallW, 56);
                LayoutPinballTotalLabel();
                return;
            }

            _pinballEyebrow.SetBounds(pad, 22, 180, 18);
            _pinballTitle.SetBounds(pad - 2, 46, width - (pad * 2) + 2, 32);
            _pinballDesc.SetBounds(pad, 80, width - (pad * 2), 50);
            int totalLabelW = width < 380 ? 88 : 116;
            _pinballInputLabel.SetBounds(pad, 138, Math.Max(120, width - (pad * 2) - totalLabelW - 8), 20);
            _pinballCountLabel.SetBounds(width - pad - totalLabelW, 138, totalLabelW, 20);

            int textY = 162;
            int textHeight = Math.Max(82, height - 304);
            _pinballText.SetBounds(pad, textY, width - (pad * 2), textHeight);

            int buttonY = textY + textHeight + 18;
            _openPinballButton.SetBounds(pad, buttonY, width - (pad * 2), 46);

            int smallY = buttonY + 62;
            int smallGap = 10;
            int smallW = (width - (pad * 2) - smallGap) / 2;
            _copyButton.SetBounds(pad, smallY, smallW, 36);
            _saveButton.SetBounds(pad + smallW + smallGap, smallY, smallW, 36);
            LayoutPinballTotalLabel();

        }

        private void LayoutPinballTotalLabel()
        {
            if (_pinballText == null || _pinballInputLabel == null || _pinballCountLabel == null)
            {
                return;
            }

            // 합계 자릿수가 늘어도 오른쪽 정렬을 유지하고 '코인'까지 표시한다.
            int measuredWidth = TextRenderer.MeasureText(_pinballCountLabel.Text, _pinballCountLabel.Font).Width + 4;
            int availableWidth = Math.Max(92, _pinballText.Right - _pinballInputLabel.Left - 120);
            int labelWidth = Math.Min(availableWidth, Math.Max(92, measuredWidth));
            _pinballCountLabel.Width = labelWidth;
            _pinballCountLabel.Left = _pinballText.Right - labelWidth;
            _pinballInputLabel.Width = Math.Max(0, _pinballCountLabel.Left - _pinballInputLabel.Left - 8);
        }

        private void LayoutFooter(int x, int y, int width)
        {
            int footerY = y;
            int creditTextW = TextRenderer.MeasureText(
                _footerRight.Text,
                _footerRight.Font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int creditW = Math.Max(48, creditTextW + 27);
            _footerLeft.SetBounds(x, footerY, Math.Max(180, width - creditW - 16), 18);
            _footerRight.SetBounds(x + width - creditW, footerY + 3, creditW, 18);
            _footerRight.Visible = width >= 560;
        }

        private async void StartConnection()
        {
            string raw = _streamInput.Text.Trim();
            if (raw.Length == 0)
            {
                using (var dialog = new InputRequiredDialog(Font, _text, _muted, _purple, _line))
                {
                    dialog.ShowDialog(this);
                }
                return;
            }

            _manualDisconnect = false;
            _connected = false;
            _connecting = true;
            _reconnectTimer.Stop();
            _connectTimer.Stop();
            CloseChatClient();
            string nextStreamerId = ExtractStreamerId(raw);
            if (!String.Equals(_streamerId, nextStreamerId, StringComparison.OrdinalIgnoreCase))
            {
                _pending.Clear();
            }
            _streamerId = nextStreamerId;
            _streamerName = ResolveDisplayName(_streamerId);
                RefreshStatus("● " + _streamerName + " 방송 확인 중", _amber, Color.FromArgb(255, 246, 239));
            ShowToast(_streamerName + " 방송 정보를 확인합니다.");
            RefreshConnectionButton();

            var client = new SoopLiveChatClient();
            _chatClient = client;
            AttachChatClient(client);

            try
            {
                await client.ConnectAsync(_streamerId, "");
            }
            catch (Exception ex)
            {
                if (_chatClient != client)
                {
                    return;
                }

                CloseChatClient();
                _connected = false;
                _connecting = false;
                RefreshConnectionButton();
                RefreshStatus("● " + _streamerName + " 연결 실패", _red, Color.FromArgb(255, 242, 247));
                ShowToast(ex.Message);
                if (!_manualDisconnect)
                {
                    ScheduleReconnect();
                }
            }
        }

        private void Disconnect(bool manual)
        {
            _manualDisconnect = manual;
            _connected = false;
            _connecting = false;
            _connectTimer.Stop();
            _reconnectTimer.Stop();
            CloseChatClient();
            RefreshConnectionButton();
            RefreshStatus("● 연결 안 됨", _muted, Color.FromArgb(255, 246, 248));
            if (manual)
            {
                ShowToast("수집을 중지했습니다.");
            }
        }

        private void ScheduleReconnect()
        {
            if (_manualDisconnect)
            {
                return;
            }

            _reconnectAttempt++;
            int wait = Math.Min(12000, 1000 + (_reconnectAttempt * 1500));
            _reconnectTimer.Interval = wait;
            RefreshStatus("● " + _streamerName + " 재연결 대기", _amber, Color.FromArgb(255, 246, 239));
            _reconnectTimer.Start();
            RefreshConnectionButton();
        }

        private void AttachChatClient(SoopLiveChatClient client)
        {
            client.BroadcastResolved += delegate(SoopLiveInfo info)
            {
                RunOnUiThread(delegate
                {
                    if (_chatClient != client)
                    {
                        return;
                    }

                    _streamerName = info.StreamerName;
                    RefreshStatus("● " + _streamerName + " 채팅 연결 중", _amber, Color.FromArgb(255, 246, 239));
                    ShowToast(GetStartToastText());
                });
            };

            client.Joined += delegate
            {
                RunOnUiThread(delegate
                {
                    if (_chatClient != client)
                    {
                        return;
                    }

                    _connected = true;
                    _connecting = false;
                    _reconnectAttempt = 0;
                    RefreshConnectionButton();
                    RefreshStatus("● " + _streamerName + " 연결됨", _green, Color.FromArgb(235, 250, 245));
                    ShowToast(_streamerName + " 방송 채팅에 연결됐습니다.");
                });
            };

            client.BalloonReceived += delegate(GiftSource source, string nickname, int count)
            {
                RunOnUiThread(delegate
                {
                    if (_chatClient == client)
                    {
                        HandleBalloonGift(source, nickname, count);
                    }
                });
            };

            client.ChatReceived += delegate(string nickname, string message)
            {
                RunOnUiThread(delegate
                {
                    if (_chatClient == client)
                    {
                        HandleChatMessage(nickname, message);
                    }
                });
            };

            client.Disconnected += delegate(string reason)
            {
                RunOnUiThread(delegate
                {
                    if (_chatClient != client)
                    {
                        return;
                    }

                    CloseChatClient();
                    _connected = false;
                    _connecting = false;
                    RefreshConnectionButton();
                    if (!_manualDisconnect)
                    {
                        RefreshStatus("● " + _streamerName + " 연결 끊김", _amber, Color.FromArgb(255, 246, 239));
                        ShowToast("채팅 연결이 끊겨 자동 재연결합니다.");
                        ScheduleReconnect();
                    }
                });
            };
        }

        private void CloseChatClient()
        {
            SoopLiveChatClient client = _chatClient;
            _chatClient = null;
            if (client != null)
            {
                client.Dispose();
            }
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed || action == null)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch
                {
                }
                return;
            }

            action();
        }

        private void AddManualTestEntry()
        {
            var entry = new CollectedEntry();
            entry.Source = GiftSource.StarBalloon;
            entry.Nickname = "테스트";
            entry.BalloonCount = _thresholdInput.Value;
            entry.CoinCount = CalculateCoins(entry.BalloonCount);
            entry.PinballName = _nicknamePinballMode ? "테스트" : "종겜핀볼 테스트";
            entry.ReceivedAt = DateTime.Now.ToString("HH:mm:ss");
            AddCollectedEntry(entry);
            ShowToast("수동 테스트 1건을 추가했습니다.");
        }

        private void HandleBalloonGift(GiftSource source, string nickname, int count)
        {
            if (!IsGiftSourceEnabled(source))
            {
                return;
            }

            if (nickname.Length == 0)
            {
                nickname = "익명";
            }

            int threshold = _thresholdInput.Value;
            bool passed = _exactMode
                ? count >= threshold && count % threshold == 0
                : count >= threshold;
            if (!passed)
            {
                return;
            }

            if (_nicknamePinballMode)
            {
                var entry = new CollectedEntry();
                entry.Source = source;
                entry.Nickname = nickname;
                entry.BalloonCount = count;
                entry.CoinCount = CalculateCoins(count);
                entry.PinballName = nickname;
                entry.ReceivedAt = DateTime.Now.ToString("HH:mm:ss");
                AddCollectedEntry(entry);
                return;
            }

            var gift = new PendingGift();
            gift.Source = source;
            gift.Nickname = nickname;
            gift.BalloonCount = count;
            gift.CoinCount = CalculateCoins(count);
            gift.CreatedAt = DateTime.Now;
            _pending.Add(gift);
            PrunePendingGifts();
        }

        private void HandleChatMessage(string nickname, string message)
        {
            if (nickname.Length == 0)
            {
                nickname = "익명";
            }

            if (message.Length == 0)
            {
                message = nickname;
            }

            PrunePendingGifts();
            PendingGift matched = null;
            // 같은 닉네임의 대기 후원은 수신 순서대로 채팅 1회씩 소비한다.
            for (int i = 0; i < _pending.Count; i++)
            {
                if (String.Equals(_pending[i].Nickname, nickname, StringComparison.OrdinalIgnoreCase))
                {
                    matched = _pending[i];
                    _pending.RemoveAt(i);
                    break;
                }
            }

            if (matched == null)
            {
                return;
            }

            var entry = new CollectedEntry();
            entry.Source = matched.Source;
            entry.Nickname = matched.Nickname;
            entry.BalloonCount = matched.BalloonCount;
            entry.CoinCount = matched.CoinCount;
            // 대기 목록에는 채팅 내용 모드에서 받은 후원만 들어온다.
            entry.PinballName = message;
            entry.ReceivedAt = DateTime.Now.ToString("HH:mm:ss");
            AddCollectedEntry(entry);
        }

        private void AddCollectedEntry(CollectedEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _entries.Add(entry);
            _scrollEntryListToBottomAfterRender = !IsEditingEntryList();
            AppendPinballText(entry, _scrollEntryListToBottomAfterRender);
            RefreshEntryListAfterAppend(entry);
        }

        private bool IsEditingEntryList()
        {
            if (_entryList == null)
            {
                return false;
            }

            foreach (Control control in _entryList.Controls)
            {
                EntryRowControl row = control as EntryRowControl;
                if (row != null && row.IsEditing)
                {
                    return true;
                }
            }

            return false;
        }

        private void PrunePendingGifts()
        {
            // 오래된 후원이 이후의 무관한 채팅에 연결되지 않도록 대기 시간을 제한한다.
            DateTime cutoff = DateTime.Now.AddMinutes(-10);
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].CreatedAt < cutoff)
                {
                    _pending.RemoveAt(i);
                }
            }
        }

        private int CalculateCoins(int balloonCount)
        {
            int unit = GetCoinUnit();
            int baseCoins = balloonCount / unit;
            return Math.Max(1, baseCoins);
        }

        private int GetCoinUnit()
        {
            if (_thresholdInput == null)
            {
                return 100;
            }

            return Math.Max(1, _thresholdInput.Value);
        }

        private void RecalculateCoins()
        {
            foreach (CollectedEntry entry in _entries)
            {
                if (entry != null)
                {
                    entry.CoinCount = CalculateCoins(entry.BalloonCount);
                }
            }

            foreach (PendingGift gift in _pending)
            {
                if (gift != null)
                {
                    gift.CoinCount = CalculateCoins(gift.BalloonCount);
                }
            }
        }

        private void RefreshPinballText()
        {
            RefreshPinballText(false);
        }

        private void RefreshPinballText(bool scrollToBottom)
        {
            _generatedPinballText.Clear();
            _generatedPinballCoinTotal = 0;
            foreach (CollectedEntry entry in _entries)
            {
                string part = BuildPinballEntryText(entry);
                if (part.Length > 0)
                {
                    if (_generatedPinballText.Length > 0)
                    {
                        _generatedPinballText.Append(',');
                    }
                    _generatedPinballText.Append(part);
                    _generatedPinballCoinTotal += entry.CoinCount;
                }
            }

            ApplyGeneratedPinballText(scrollToBottom);
        }

        private string BuildPinballEntryText(CollectedEntry entry)
        {
            if (entry == null || entry.CoinCount <= 0)
            {
                return "";
            }

            string name = SanitizePinballName(entry.PinballName);
            if (name.Length == 0)
            {
                name = SanitizePinballName(entry.Nickname);
            }

            return name.Length == 0 ? "" : name + "*" + entry.CoinCount;
        }

        private void AppendPinballText(CollectedEntry entry, bool scrollToBottom)
        {
            string part = BuildPinballEntryText(entry);
            if (part.Length > 0)
            {
                string suffix = (_generatedPinballText.Length > 0 ? "," : "") + part;
                _generatedPinballText.Append(suffix);
                _generatedPinballCoinTotal += entry.CoinCount;

                if (_pinballTextMatchesGenerated)
                {
                    _updatingPinballText = true;
                    try
                    {
                        if (scrollToBottom)
                        {
                            _pinballText.AppendTextScrollToBottom(suffix);
                        }
                        else
                        {
                            _pinballText.AppendTextPreserveView(suffix);
                        }
                    }
                    finally
                    {
                        _updatingPinballText = false;
                    }
                    RefreshPinballTotalLabel();
                    return;
                }
            }

            ApplyGeneratedPinballText(scrollToBottom);
        }

        private void ApplyGeneratedPinballText(bool scrollToBottom)
        {
            string nextText = _generatedPinballText.ToString();
            _updatingPinballText = true;
            try
            {
                if (_pinballText.Text != nextText)
                {
                    if (scrollToBottom)
                    {
                        _pinballText.SetTextScrollToBottom(nextText);
                    }
                    else
                    {
                        _pinballText.SetTextPreserveView(nextText);
                    }
                }
            }
            finally
            {
                _updatingPinballText = false;
            }
            _pinballTextMatchesGenerated = true;
            RefreshPinballTotalLabel();
        }

        private void RefreshCounts()
        {
            RebuildVisibleEntries();
            RefreshPinballTotalLabel();

            bool searchActive = GetSearchText().Length > 0;
            bool visibilityChanged = RefreshCollectionState(searchActive);
            RenderEntryRows();
            if (visibilityChanged && _surface != null && _surface.IsHandleCreated)
            {
                LayoutUi();
            }
        }

        private void RefreshEntryListAfterAppend(CollectedEntry entry)
        {
            string query = GetSearchText();
            if (!String.Equals(query, _visibleEntriesSearchText, StringComparison.Ordinal))
            {
                RefreshCounts();
                return;
            }

            bool searchActive = query.Length > 0;
            bool entryVisible = !searchActive || EntryMatchesSearch(entry, query);
            if (entryVisible)
            {
                _visibleEntries.Add(entry);
            }

            bool visibilityChanged = RefreshCollectionState(searchActive);
            if (visibilityChanged && _surface != null && _surface.IsHandleCreated)
            {
                LayoutUi();
            }

            if (!entryVisible)
            {
                _scrollEntryListToBottomAfterRender = false;
                return;
            }

            int previousRowCount = _entryList.Controls.Count;
            GrowEntryRowPoolForViewport();
            if (_entryList.Controls.Count == previousRowCount)
            {
                LayoutEntryRows();
            }
        }

        private bool RefreshCollectionState(bool searchActive)
        {
            bool hasVisibleEntries = _visibleEntries.Count > 0;
            bool visibilityChanged = _entryList.Visible != hasVisibleEntries;
            _entryList.Visible = hasVisibleEntries;
            _emptyState.Visible = !hasVisibleEntries;
            if (searchActive)
            {
                _searchCountLabel.Text = _visibleEntries.Count + "/" + _entries.Count;
            }
            else
            {
                _searchCountLabel.Text = _entries.Count > 0 ? _entries.Count + "개" : "";
            }
            _searchCountLabel.Visible = false;
            RefreshEmptyStateText(searchActive);
            return visibilityChanged;
        }

        private void RefreshPinballTotalLabel()
        {
            if (_pinballCountLabel != null)
            {
                long total = (_updatingPinballText || _pinballTextMatchesGenerated)
                    ? _generatedPinballCoinTotal
                    : CalculateReflectedCoinTotal();
                _pinballCountLabel.Text = "총 " + total.ToString("#,0") + "코인";
                LayoutPinballTotalLabel();
            }
        }

        private long CalculateReflectedCoinTotal()
        {
            if (_pinballText != null)
            {
                return CalculatePinballTextCoinTotal(NormalizePinballNames(_pinballText.Text));
            }

            long total = 0;
            foreach (CollectedEntry entry in _entries)
            {
                if (entry == null || entry.CoinCount <= 0)
                {
                    continue;
                }

                string name = SanitizePinballName(entry.PinballName);
                if (name.Length == 0)
                {
                    name = SanitizePinballName(entry.Nickname);
                }

                if (name.Length > 0)
                {
                    total += entry.CoinCount;
                }
            }

            return total;
        }

        private long CalculatePinballTextCoinTotal(string value)
        {
            if (value == null || value.Trim().Length == 0)
            {
                return 0;
            }

            long total = 0;
            string[] parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                Match match = Regex.Match(part, "\\*([0-9]+)\\s*$");
                if (match.Success)
                {
                    long coins;
                    if (Int64.TryParse(match.Groups[1].Value, out coins) && coins > 0)
                    {
                        total += coins;
                        continue;
                    }

                    if (coins == 0)
                    {
                        continue;
                    }
                }

                total += 1;
            }

            return total;
        }

        private void RebuildVisibleEntries()
        {
            _visibleEntries.Clear();
            string query = GetSearchText();
            _visibleEntriesSearchText = query;
            foreach (CollectedEntry entry in _entries)
            {
                if (query.Length == 0 || EntryMatchesSearch(entry, query))
                {
                    _visibleEntries.Add(entry);
                }
            }
        }

        private string GetSearchText()
        {
            return _searchInput == null ? "" : (_searchInput.Text ?? "").Trim();
        }

        private bool EntryMatchesSearch(CollectedEntry entry, string query)
        {
            if (entry == null)
            {
                return false;
            }

            return ContainsSearch(entry.Nickname, query) ||
                   ContainsSearch(entry.PinballName, query) ||
                   ContainsSearch(GiftSourceInfo.GetLabel(entry.Source), query) ||
                   ContainsSearch(entry.BalloonCount.ToString(), query) ||
                   ContainsSearch(entry.CoinCount.ToString() + "코인", query);
        }

        private static bool ContainsSearch(string value, string query)
        {
            return (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshEmptyStateText(bool searchActive)
        {
            if (searchActive && _entries.Count > 0)
            {
                _emptyTitle.Text = "검색 결과가 없어요";
                _emptyText.Text = "닉네임, 핀볼 내용, 후원 종류, 코인 기준으로\r\n다시 검색해 보세요.";
                return;
            }

            _emptyTitle.Text = "아직 수집된 채팅이 없어요";
            _emptyText.Text = "정해진 별풍선 조건을 보낸 시청자의\r\n다음 채팅 1회가 여기에 추가됩니다.";
        }

        private void RefreshStatus(string text, Color textColor, Color fillColor)
        {
            _statusPill.Text = text;
            _statusPill.ForeColor = textColor;
            _statusPill.FillColor = fillColor;
            _statusPill.BorderColor = Color.FromArgb(242, 193, 205);
            _statusPill.Invalidate();
            if (_surface != null && _surface.IsHandleCreated)
            {
                LayoutUi();
            }
        }

        private void RefreshConnectionButton()
        {
            if (_connectButton == null)
            {
                return;
            }

            bool collecting = _connected || _connecting || _connectTimer.Enabled || _reconnectTimer.Enabled;
            if (collecting)
            {
                _connectButton.Text = "수집 중지";
                _connectButton.Glyph = ButtonGlyph.Stop;
                _connectButton.FillColor = Color.FromArgb(255, 232, 238);
                _connectButton.GradientColor = Color.Empty;
                _connectButton.BorderColor = Color.FromArgb(239, 165, 181);
                _connectButton.ForeColor = Color.FromArgb(139, 40, 67);
                _connectButton.UseGradient = false;
            }
            else
            {
                _connectButton.Text = "수집 시작";
                _connectButton.Glyph = ButtonGlyph.Play;
                _connectButton.FillColor = Color.FromArgb(230, 70, 105);
                _connectButton.GradientColor = Color.FromArgb(250, 113, 122);
                _connectButton.BorderColor = Color.FromArgb(236, 138, 157);
                _connectButton.ForeColor = Color.White;
                _connectButton.UseGradient = true;
            }

            _connectButton.Invalidate();
        }

        private bool IsPreviewMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--preview", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsPreviewManyMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--preview-many", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsToastPreviewMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--toast-preview", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsCollectingPreviewMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--collecting-preview", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsManualTestPreviewMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--manual-test-preview", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsClearDialogPreviewMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--clear-dialog-preview", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsTargetDialogPreviewMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--target-dialog-preview", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsConnectingPreviewMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], "--connecting-preview", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string GetArgumentValue(string prefix)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(prefix.Length).Trim();
                }
            }

            return "";
        }

        private int GetArgumentInt(string prefix, int fallback, int max)
        {
            string value = GetArgumentValue(prefix);
            int parsed;
            if (!Int32.TryParse(value, out parsed))
            {
                return fallback;
            }

            return Math.Max(0, Math.Min(max, parsed));
        }

        private string GetStartToastText()
        {
            int threshold = _thresholdInput.Value;
            string name = _streamerName.Length > 0 ? _streamerName : "방송";
            string condition = _exactMode ? "정확히 " + threshold + "개" : threshold + "개 이상";
            return name + " 방송 연결 중 · " + condition + " 수집";
        }

        private void ShowToast(string message)
        {
            if (_toast == null || message == null || message.Length == 0)
            {
                return;
            }

            _toast.Text = message;
            RelayoutToast();
            _toast.Visible = true;
            _toast.BringToFront();
            _toast.Invalidate();
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void RelayoutToast()
        {
            int viewportWidth = Math.Max(320, _surface.Width);
            int margin = viewportWidth < 520 ? 8 : 18;
            int contentWidth = Math.Max(300, viewportWidth - (margin * 2));
            if (contentWidth < 920)
            {
                contentWidth = Math.Max(300, contentWidth - SystemInformation.VerticalScrollBarWidth);
            }

            int y = viewportWidth < 520 ? 10 : 16;
            LayoutToast(margin, y, contentWidth);
        }

        private void SeedPreviewRows()
        {
            _entries.Add(new CollectedEntry { Source = GiftSource.StarBalloon, Nickname = "테스트", BalloonCount = 100, CoinCount = 1, PinballName = "테스트", ReceivedAt = DateTime.Now.ToString("HH:mm:ss") });
            _entries.Add(new CollectedEntry { Source = GiftSource.AdBalloon, Nickname = "마이곰이", BalloonCount = 1017, CoinCount = 10, PinballName = "마이곰이 종겜핀볼", ReceivedAt = DateTime.Now.ToString("HH:mm:ss") });
            _entries.Add(new CollectedEntry { Source = GiftSource.ChallengeGift, Nickname = "시소즈", BalloonCount = 200, CoinCount = 2, PinballName = "테스트2", ReceivedAt = DateTime.Now.ToString("HH:mm:ss") });
            RefreshPinballText();
        }

        private void SeedManyPreviewRows()
        {
            SeedManyPreviewRows(12);
        }

        private void SeedManyPreviewRows(int count)
        {
            for (int i = 1; i <= count; i++)
            {
                int balloons = i == 1 ? 100 : i * 100;
                _entries.Add(new CollectedEntry
                {
                    Source = (GiftSource)((i - 1) % 3),
                    Nickname = "테스트" + i,
                    BalloonCount = balloons,
                    CoinCount = CalculateCoins(balloons),
                    PinballName = "종겜핀볼 테스트 " + i,
                    ReceivedAt = DateTime.Now.ToString("HH:mm:ss")
                });
            }

            RefreshPinballText();
        }

        private void AddStressEntries(int count)
        {
            for (int i = 1; i <= count; i++)
            {
                int balloons = i == 1 ? 100 : i * 100;
                var entry = new CollectedEntry();
                entry.Source = (GiftSource)((i - 1) % 3);
                entry.Nickname = "테스트" + i;
                entry.BalloonCount = balloons;
                entry.CoinCount = CalculateCoins(balloons);
                entry.PinballName = "종겜핀볼 스트레스 " + i;
                entry.ReceivedAt = DateTime.Now.ToString("HH:mm:ss");
                AddCollectedEntry(entry);
            }

            ShowToast(count + "개 항목 테스트를 완료했습니다.");
        }

        private void RefreshModeButtons()
        {
            StyleSegment(_exactButton, _exactMode);
            StyleSegment(_atLeastButton, !_exactMode);
        }

        private void RefreshSourceButtons()
        {
            StyleSegment(_nicknameSourceButton, _nicknamePinballMode);
            StyleSegment(_contentSourceButton, !_nicknamePinballMode);
        }

        private void ToggleTargetPopup()
        {
            if (_targetPopup.Visible)
            {
                HideTargetPopup();
                return;
            }

            ShowTargetPopup();
        }

        private void ShowTargetPopup()
        {
            _targetPopup.SetSelections(_collectStarBalloon, _collectAdBalloon, _collectChallengeGift);
            LayoutTargetPopup();
            RefreshTargetPopupBackdrop();
            _targetPopup.Visible = true;
            _targetPopup.BringToFront();
            RefreshTargetButton();
        }

        private void HideTargetPopup()
        {
            if (_targetPopup == null || !_targetPopup.Visible)
            {
                return;
            }

            _targetPopup.Visible = false;
            RefreshTargetButton();
        }

        private void RefreshTargetButton()
        {
            int selected = (_collectStarBalloon ? 1 : 0) +
                           (_collectAdBalloon ? 1 : 0) +
                           (_collectChallengeGift ? 1 : 0);
            string arrow = _targetPopup != null && _targetPopup.Visible ? "◀" : "▶";
            _targetButton.Text = "수집 대상 " + selected + "/3 " + arrow;
            StyleSegment(_targetButton, selected == 3);
        }

        private bool IsGiftSourceEnabled(GiftSource source)
        {
            if (source == GiftSource.AdBalloon)
            {
                return _collectAdBalloon;
            }

            if (source == GiftSource.ChallengeGift)
            {
                return _collectChallengeGift;
            }

            return _collectStarBalloon;
        }

        private void StyleSegment(RoundButton button, bool active)
        {
            if (button == null)
            {
                return;
            }

            if (active)
            {
                button.FillColor = _lavender;
                button.BorderColor = Color.FromArgb(239, 159, 176);
                button.ForeColor = _purpleDark;
            }
            else
            {
                button.FillColor = Color.FromArgb(255, 252, 250);
                button.BorderColor = _line;
                button.ForeColor = _muted;
            }
            button.Invalidate();
        }

        private void CopyPinballText()
        {
            string text = _pinballText.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("복사할 핀볼 입력값이 없습니다.", "목록 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Clipboard.SetText(text);
        }

        private void SavePinballText()
        {
            string text = _pinballText.Text.Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("저장할 핀볼 입력값이 없습니다.", "목록 없음", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var dialog = new SaveFileDialog();
            dialog.Title = "핀볼 입력값 저장";
            dialog.Filter = "Text file (*.txt)|*.txt";
            dialog.FileName = "pinball-list.txt";
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllText(dialog.FileName, text, Encoding.UTF8);
            }
            dialog.Dispose();
        }

        private void OpenPinballSite()
        {
            if (PinballUrl.Length == 0)
            {
                ShowToast("핀볼 사이트 주소가 아직 설정되지 않았습니다.");
                return;
            }

            try
            {
                string names = NormalizePinballNames(_pinballText.Text);
                if (names.Length == 0)
                {
                    Process.Start(PinballUrl);
                    return;
                }

                string url = BuildPinballSiteUrl(names);
                if (url.Length <= DirectPinballUrlLimit)
                {
                    Process.Start(url);
                    return;
                }

                Clipboard.SetText(names);
                Process.Start(PinballUrl);
                ShowToast("목록이 길어 클립보드에 복사했습니다. 핀볼 사이트에 붙여넣어 주세요.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "사이트 열기 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string BuildPinballSiteUrl()
        {
            string names = NormalizePinballNames(_pinballText.Text);
            return BuildPinballSiteUrl(names);
        }

        private string BuildPinballSiteUrl(string names)
        {
            if (names.Length == 0)
            {
                return PinballUrl;
            }

            return PinballUrl + "?names=" + Uri.EscapeDataString(names);
        }

        private string NormalizePinballNames(string value)
        {
            if (value == null)
            {
                return "";
            }

            string[] rawParts = value.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var parts = new List<string>();
            foreach (string rawPart in rawParts)
            {
                string part = rawPart.Trim();
                if (part.Length > 0)
                {
                    parts.Add(part);
                }
            }

            return String.Join(",", parts.ToArray());
        }

        private void ClearEntries()
        {
            if (_entries.Count == 0 && _pending.Count == 0)
            {
                return;
            }

            using (var dialog = new ClearEntriesDialog(Font, _text, _muted, _purple, _line))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            if (_entries.Count == 0 && _pending.Count == 0)
            {
                return;
            }

            _entries.Clear();
            _pending.Clear();
            RefreshPinballText();
            RefreshCounts();
        }

        private string ExtractStreamerId(string raw)
        {
            string trimmed = (raw ?? "").Trim();
            if (Regex.IsMatch(trimmed, "^[A-Za-z0-9_\\-]+$"))
            {
                return trimmed;
            }

            string address = trimmed;
            if (!Regex.IsMatch(address, "^[A-Za-z][A-Za-z0-9+\\-.]*://"))
            {
                address = "https://" + address;
            }

            Uri uri;
            if (Uri.TryCreate(address, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                string[] parts = uri.AbsolutePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                int startIndex = parts.Length > 0 &&
                    String.Equals(parts[0], "station", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                for (int i = startIndex; i < parts.Length; i++)
                {
                    string part = Uri.UnescapeDataString(parts[i]).Trim();
                    if (Regex.IsMatch(part, "^[A-Za-z0-9_\\-]+$") && !Regex.IsMatch(part, "^\\d+$"))
                    {
                        return part;
                    }
                }

                return "";
            }

            return Regex.Replace(trimmed, "[^A-Za-z0-9_\\-]", "");
        }

        private string ResolveDisplayName(string streamerId)
        {
            if (streamerId == null || streamerId.Length == 0)
            {
                return "알 수 없음";
            }

            return streamerId;
        }

        private string SanitizePinballName(string value)
        {
            if (value == null)
            {
                return "";
            }

            string cleaned = value.Trim();
            cleaned = cleaned.Replace(",", " ");
            cleaned = cleaned.Replace("*", "x");
            cleaned = Regex.Replace(cleaned, "\\s+", " ");
            return cleaned;
        }


        private string GetConditionToastText()
        {
            int threshold = _thresholdInput.Value;
            return _exactMode ? "정확히 " + threshold + "개만 수집합니다." : threshold + "개 이상을 모두 수집합니다.";
        }

        private string BuildCoinGuideText()
        {
            int unit = GetCoinUnit();
            int tenCoinAt = unit * 10;
            return "별풍선 " + unit + "개 = 1코인\r\n" + tenCoinAt + "개 = 10코인";
        }

        private void RefreshCoinGuide()
        {
            if (_pinballDesc != null)
            {
                _pinballDesc.Text = BuildCoinGuideText();
            }
        }

        private RoundedPanel Card()
        {
            var panel = new RoundedPanel();
            panel.Radius = 16;
            panel.FillColor = _card;
            panel.BorderColor = Color.FromArgb(241, 186, 199);
            return panel;
        }


        private Label PlainLabel(string text, float size, FontStyle style, Color color)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Color.Transparent;
            label.ForeColor = color;
            label.Font = UiFont.Make(size, style);
            label.AutoEllipsis = true;
            return label;
        }

        private Label SmallHeader(string text)
        {
            return PlainLabel(text, 9.5f, FontStyle.Bold, _text);
        }

        private Label Eyebrow(string text)
        {
            return PlainLabel(text, 7.9f, FontStyle.Bold, _purpleDark);
        }

        private RoundButton BaseButton(string text)
        {
            var button = new RoundButton();
            button.Text = text;
            button.Font = UiFont.Make(9.2f, FontStyle.Bold);
            button.Radius = 9;
            button.CanvasColor = _card;
            return button;
        }

        private RoundButton PrimaryButton(string text)
        {
            var button = BaseButton(text);
            button.FillColor = Color.FromArgb(229, 65, 101);
            button.BorderColor = Color.FromArgb(229, 65, 101);
            button.ForeColor = Color.White;
            return button;
        }

        private RoundButton GradientButton(string text)
        {
            var button = BaseButton(text);
            button.UseGradient = true;
            button.FillColor = Color.FromArgb(230, 70, 105);
            button.GradientColor = Color.FromArgb(250, 113, 122);
            button.BorderColor = Color.FromArgb(236, 138, 157);
            button.ForeColor = Color.White;
            return button;
        }

        private RoundButton SecondaryButton(string text)
        {
            var button = BaseButton(text);
            button.FillColor = Color.FromArgb(255, 252, 250);
            button.BorderColor = _line;
            button.ForeColor = _muted;
            return button;
        }

        private RoundButton SegmentButton(string text)
        {
            var button = BaseButton(text);
            button.Font = UiFont.Make(9.5f, FontStyle.Bold);
            return button;
        }

        private RoundButton HeaderActionButton(string text, bool primary)
        {
            var button = BaseButton(text);
            button.Font = UiFont.Make(8.9f, FontStyle.Bold);
            button.Radius = 10;
            if (primary)
            {
                button.FillColor = Color.FromArgb(255, 241, 243);
                button.BorderColor = Color.FromArgb(239, 165, 181);
                button.ForeColor = Color.FromArgb(139, 40, 67);
            }
            else
            {
                button.FillColor = Color.FromArgb(255, 252, 250);
                button.BorderColor = _line;
                button.ForeColor = _muted;
            }

            return button;
        }



    }

    internal sealed class SoopLiveInfo
    {
        public string StreamerId;
        public string StreamerName;
        public string Title;
        public string ChatNo;
        public string Token;
        public string Host;
        public int Port;
        public string Bps;
        public string GeoCc;
        public string GeoRc;
        public string AcceptLanguage;
        public string ServiceLanguage;
    }

    internal sealed class SoopLiveChatClient : IDisposable
    {
        private const string Fs = "\u000c";
        private const string Esc = "\u001b";
        private const string Tab = "\u0009";
        private const string CmdConnect = "\u001b\u0009000100000600\u000c\u000c\u000c16\u000c";

        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private ClientWebSocket _socket;
        private CancellationTokenSource _cts;
        private SoopLiveInfo _info;
        private bool _disposed;
        private bool _joinedRaised;

        public event Action<SoopLiveInfo> BroadcastResolved = delegate { };
        public event Action Joined = delegate { };
        public event Action<GiftSource, string, int> BalloonReceived = delegate { };
        public event Action<string, string> ChatReceived = delegate { };
        public event Action<string> Disconnected = delegate { };

        public async Task ConnectAsync(string streamerId, string password)
        {
            if (String.IsNullOrWhiteSpace(streamerId))
            {
                throw new InvalidOperationException("SOOP ID가 비어 있습니다.");
            }

            _disposed = false;
            _joinedRaised = false;
            _cts = new CancellationTokenSource();
            _info = await LoadLiveInfoAsync(streamerId.Trim(), password ?? "");
            BroadcastResolved(_info);

            string wsUrl = "wss://" + _info.Host.ToLowerInvariant() + ":" + _info.Port + "/Websocket/" + _info.StreamerId;
            _socket = new ClientWebSocket();
            _socket.Options.AddSubProtocol("chat");
            await _socket.ConnectAsync(new Uri(wsUrl), _cts.Token);
            await SendRawAsync(CmdConnect);
            StartBackgroundLoops();
        }

        public void Dispose()
        {
            _disposed = true;
            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                }
            }
            catch
            {
            }

            try
            {
                if (_socket != null)
                {
                    _socket.Abort();
                    _socket.Dispose();
                }
            }
            catch
            {
            }
        }

        private void StartBackgroundLoops()
        {
            Task.Run((Func<Task>)ReceiveLoopAsync);
            Task.Run((Func<Task>)PingLoopAsync);
        }

        private async Task<SoopLiveInfo> LoadLiveInfoAsync(string streamerId, string password)
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;

            string json = await Task.Run(delegate
            {
                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                    client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                    var values = new NameValueCollection();
                    values["bid"] = streamerId;
                    values["type"] = "live";
                    values["pwd"] = password ?? "";
                    values["player_type"] = "html5";
                    values["stream_type"] = "common";
                    values["mode"] = "landing";
                    values["from_api"] = "0";
                    byte[] response = client.UploadValues("https://live.sooplive.co.kr/afreeca/player_live_api.php?bjid=" + Uri.EscapeDataString(streamerId), "POST", values);
                    return Encoding.UTF8.GetString(response);
                }
            });

            var root = _serializer.DeserializeObject(json) as Dictionary<string, object>;
            Dictionary<string, object> channel = GetObject(root, "CHANNEL");
            if (channel == null)
            {
                throw new InvalidOperationException("SOOP 방송 정보를 읽지 못했습니다.");
            }

            int result = GetInt(channel, "RESULT");
            if (result != 1)
            {
                throw new InvalidOperationException(streamerId + " 방송이 현재 라이브 상태가 아닙니다.");
            }

            string host = GetString(channel, "CHDOMAIN");
            if (host.Length == 0)
            {
                host = GetString(channel, "CHIP");
            }

            int chatPort = GetInt(channel, "CHPT") + 1;
            if (host.Length == 0 || chatPort <= 1)
            {
                throw new InvalidOperationException("채팅 서버 정보를 찾지 못했습니다.");
            }

            var info = new SoopLiveInfo();
            info.StreamerId = GetString(channel, "BJID");
            if (info.StreamerId.Length == 0)
            {
                info.StreamerId = streamerId;
            }
            info.StreamerName = GetString(channel, "BJNICK");
            if (info.StreamerName.Length == 0)
            {
                info.StreamerName = info.StreamerId;
            }
            info.Title = GetString(channel, "TITLE");
            info.ChatNo = GetString(channel, "CHATNO");
            info.Token = GetString(channel, "FTK");
            info.Host = host;
            info.Port = chatPort;
            info.Bps = GetString(channel, "BPS");
            info.GeoCc = GetString(channel, "geo_cc");
            info.GeoRc = GetString(channel, "geo_rc");
            info.AcceptLanguage = GetString(channel, "acpt_lang");
            info.ServiceLanguage = GetString(channel, "svc_lang");
            return info;
        }

        private async Task ReceiveLoopAsync()
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (!_disposed && _socket != null && _socket.State == WebSocketState.Open)
                {
                    using (var stream = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                RaiseDisconnected("채팅 서버가 연결을 종료했습니다.");
                                return;
                            }

                            stream.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        byte[] packet = stream.ToArray();
                        if (packet.Length > 0)
                        {
                            await ProcessPacketAsync(packet);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                RaiseDisconnected(ex.Message);
            }
        }

        private async Task PingLoopAsync()
        {
            try
            {
                while (!_disposed && _cts != null && !_cts.IsCancellationRequested)
                {
                    await Task.Delay(60000, _cts.Token);
                    await SendPacketAsync(0, Fs);
                }
            }
            catch
            {
            }
        }

        private async Task ProcessPacketAsync(byte[] packet)
        {
            if (packet.Length < 14)
            {
                return;
            }

            string header = Encoding.UTF8.GetString(packet, 0, 14);
            int serviceCommand;
            if (!Int32.TryParse(header.Substring(2, 4), out serviceCommand))
            {
                return;
            }

            int bodyStart = packet.Length > 15 ? 15 : 14;
            string body = packet.Length > bodyStart ? Encoding.UTF8.GetString(packet, bodyStart, packet.Length - bodyStart) : "";
            string[] parts = body.Split('\u000c');

            if (serviceCommand == 1)
            {
                await SendJoinAsync();
                return;
            }

            if (serviceCommand == 2)
            {
                if (parts.Length > 0 && parts[0] == "비밀번호가 틀렸습니다.")
                {
                    RaiseDisconnected("비밀번호가 틀렸습니다.");
                    return;
                }

                if (!_joinedRaised)
                {
                    _joinedRaised = true;
                    Joined();
                }
                return;
            }

            if (serviceCommand == 5)
            {
                if (parts.Length >= 6)
                {
                    string message = parts[0];
                    string nickname = parts[5];
                    if (nickname.Length > 0 && message.Length > 0)
                    {
                        ChatReceived(nickname, message);
                    }
                }
                return;
            }

            // 후원 패킷마다 닉네임과 개수의 필드 위치가 다르므로 분기별로 검증한다.
            if (serviceCommand == 18)
            {
                if (parts.Length >= 4)
                {
                    int count;
                    if (Int32.TryParse(parts[3], out count) && count > 0)
                    {
                        BalloonReceived(GiftSource.StarBalloon, parts[2], count);
                    }
                }
                return;
            }

            if (serviceCommand == 33)
            {
                if (parts.Length >= 6)
                {
                    int count;
                    if (Int32.TryParse(parts[5], out count) && count > 0)
                    {
                        BalloonReceived(GiftSource.StarBalloon, parts[4], count);
                    }
                }
                return;
            }

            if (serviceCommand == 87)
            {
                if (parts.Length >= 10)
                {
                    string nickname = parts[3];
                    if (nickname.Length == 0)
                    {
                        nickname = parts[2];
                    }

                    int count;
                    if (nickname.Length > 0 && Int32.TryParse(parts[9], out count) && count > 0)
                    {
                        BalloonReceived(GiftSource.AdBalloon, nickname, count);
                    }
                }
                return;
            }

            if (serviceCommand == 121)
            {
                if (parts.Length > 0)
                {
                    HandleChallengeGift(parts[0]);
                }
                return;
            }

            if (serviceCommand == 88)
            {
                RaiseDisconnected("방송이 종료되었습니다.");
            }
        }

        private void HandleChallengeGift(string json)
        {
            if (String.IsNullOrWhiteSpace(json))
            {
                return;
            }

            Dictionary<string, object> gift;
            try
            {
                gift = _serializer.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch
            {
                return;
            }

            if (gift == null)
            {
                return;
            }

            if (!String.Equals(GetString(gift, "type"), "CHALLENGE_GIFT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string nickname = GetString(gift, "user_nick");
            if (nickname.Length == 0)
            {
                nickname = GetString(gift, "user_id");
            }

            int count = GetInt(gift, "gift_count");
            if (nickname.Length > 0 && count > 0)
            {
                BalloonReceived(GiftSource.ChallengeGift, nickname, count);
            }
        }

        private async Task SendJoinAsync()
        {
            if (_info == null)
            {
                return;
            }

            var body = new StringBuilder();
            body.Append(Fs).Append(_info.ChatNo);
            body.Append(Fs).Append(_info.Token);
            body.Append(Fs).Append("0").Append(Fs).Append(Fs).Append("log\u0011");
            body.Append("\u0006&\u0006set_bps\u0006=\u0006").Append(_info.Bps);
            body.Append("\u0006&\u0006view_bps\u0006=\u0006").Append(_info.Bps);
            body.Append("\u0006&\u0006quality\u0006=\u0006ori");
            body.Append("\u0006&\u0006geo_cc\u0006=\u0006").Append(_info.GeoCc);
            body.Append("\u0006&\u0006geo_rc\u0006=\u0006").Append(_info.GeoRc);
            body.Append("\u0006&\u0006acpt_lang\u0006=\u0006").Append(_info.AcceptLanguage);
            body.Append("\u0006&\u0006svc_lang\u0006=\u0006").Append(_info.ServiceLanguage);
            body.Append("\u0006&\u0006subscribe\u0006=\u00060");
            body.Append("\u0006&\u0006lowlatency\u0006=\u00061");
            body.Append("\u0012pwd\u0011\u0012");
            body.Append("auth_info\u0011NULL\u0012");
            body.Append("pver\u00112\u0012");
            body.Append("access_system\u0011html5\u0012");
            body.Append(Fs);
            await SendPacketAsync(2, body.ToString());
        }

        private async Task SendPacketAsync(int serviceCommand, string body)
        {
            string header = Esc + Tab + serviceCommand.ToString("D4") + body.Length.ToString("D6") + "00";
            await SendRawAsync(header + body);
        }

        private async Task SendRawAsync(string text)
        {
            if (_disposed || _socket == null || _socket.State != WebSocketState.Open)
            {
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        private void RaiseDisconnected(string reason)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Disconnected(reason);
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key))
            {
                return null;
            }

            return source[key] as Dictionary<string, object>;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.ContainsKey(key) || source[key] == null)
            {
                return "";
            }

            return Convert.ToString(source[key]);
        }

        private static int GetInt(Dictionary<string, object> source, string key)
        {
            string value = GetString(source, key);
            int parsed;
            return Int32.TryParse(value, out parsed) ? parsed : 0;
        }
    }

    internal sealed class GiftSourcePopup : RoundedPanel
    {
        private readonly Color _line;
        private int _backdropSplitX;
        private Color _leftBackdropColor;
        private Color _rightBackdropColor;
        private Bitmap _backdropImage;
        private readonly GiftSourceOption _starBalloon;
        private readonly GiftSourceOption _adBalloon;
        private readonly GiftSourceOption _challengeGift;

        public event EventHandler SelectionChanged;
        public event EventHandler SelectionRejected;

        public bool CollectStarBalloon { get { return _starBalloon.Checked; } }
        public bool CollectAdBalloon { get { return _adBalloon.Checked; } }
        public bool CollectChallengeGift { get { return _challengeGift.Checked; } }

        public GiftSourcePopup(
            Color text,
            Color muted,
            Color purple,
            Color line,
            bool collectStarBalloon,
            bool collectAdBalloon,
            bool collectChallengeGift)
        {
            _line = line;
            Size = new Size(244, 140);
            Radius = 12;
            FillColor = Color.FromArgb(255, 240, 243);
            BorderColor = Color.FromArgb(136, 38, 65);
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            _leftBackdropColor = Color.FromArgb(255, 237, 241);
            _rightBackdropColor = FillColor;

            var title = new Label();
            title.Text = "수집 대상";
            title.AutoSize = false;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.ForeColor = text;
            title.BackColor = Color.Transparent;
            title.Font = UiFont.Make(11.5f, FontStyle.Bold);
            title.SetBounds(14, 9, 216, 24);
            Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = "수집할 후원 종류를 선택하세요.";
            subtitle.AutoSize = false;
            subtitle.TextAlign = ContentAlignment.MiddleLeft;
            subtitle.ForeColor = muted;
            subtitle.BackColor = Color.Transparent;
            subtitle.Font = UiFont.Make(8.5f, FontStyle.Regular);
            subtitle.SetBounds(14, 32, 216, 20);
            Controls.Add(subtitle);

            _starBalloon = CreateOption("별풍선", 14, 58, 104, 32, collectStarBalloon, text, purple);
            _adBalloon = CreateOption("애드벌룬", 126, 58, 104, 32, collectAdBalloon, text, purple);
            _challengeGift = CreateOption("도전미션", 14, 98, 104, 32, collectChallengeGift, text, purple);
            Controls.Add(_starBalloon);
            Controls.Add(_adBalloon);
            Controls.Add(_challengeGift);

        }

        public void SetSelections(bool starBalloon, bool adBalloon, bool challengeGift)
        {
            _starBalloon.Checked = starBalloon;
            _adBalloon.Checked = adBalloon;
            _challengeGift.Checked = challengeGift;
        }

        public void SetBackdrop(int splitX, Color leftColor, Color rightColor)
        {
            int nextSplitX = Math.Max(0, Math.Min(Width, splitX));
            if (_backdropSplitX == nextSplitX &&
                _leftBackdropColor == leftColor &&
                _rightBackdropColor == rightColor)
            {
                return;
            }

            _backdropSplitX = nextSplitX;
            _leftBackdropColor = leftColor;
            _rightBackdropColor = rightColor;
            Invalidate();
        }

        public void SetBackdropImage(Bitmap image)
        {
            Bitmap nextBackdrop = image == null ? null : new Bitmap(image);
            Bitmap previousBackdrop = _backdropImage;
            _backdropImage = nextBackdrop;
            if (previousBackdrop != null)
            {
                previousBackdrop.Dispose();
            }
            Invalidate();
        }

        private GiftSourceOption CreateOption(string text, int x, int y, int width, int height, bool isChecked, Color foreColor, Color accent)
        {
            var option = new GiftSourceOption();
            option.Text = text;
            option.Checked = isChecked;
            option.SetBounds(x, y, width, height);
            option.Font = UiFont.Make(9.0f, FontStyle.Bold);
            option.ForeColor = foreColor;
            option.FillColor = Color.FromArgb(255, 252, 250);
            option.CheckedFillColor = Color.FromArgb(255, 225, 232);
            option.BorderColor = Color.FromArgb(237, 158, 177);
            option.AccentColor = accent;
            option.Cursor = Cursors.Hand;
            option.Click += HandleOptionClick;
            return option;
        }

        private void HandleOptionClick(object sender, EventArgs e)
        {
            if (!_starBalloon.Checked && !_adBalloon.Checked && !_challengeGift.Checked)
            {
                GiftSourceOption option = sender as GiftSourceOption;
                if (option != null)
                {
                    option.Checked = true;
                }
                if (SelectionRejected != null)
                {
                    SelectionRejected(this, EventArgs.Empty);
                }
                return;
            }

            if (SelectionChanged != null)
            {
                SelectionChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (_backdropImage != null && _backdropImage.Size == ClientSize)
            {
                e.Graphics.DrawImageUnscaled(_backdropImage, Point.Empty);
                return;
            }

            if (_backdropSplitX > 0)
            {
                using (var leftBrush = new SolidBrush(_leftBackdropColor))
                {
                    e.Graphics.FillRectangle(leftBrush, 0, 0, _backdropSplitX, Height);
                }
            }

            using (var rightBrush = new SolidBrush(_rightBackdropColor))
            {
                e.Graphics.FillRectangle(rightBrush, _backdropSplitX, 0, Width - _backdropSplitX, Height);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            const int renderScale = 4;
            const int borderWidth = 2;
            int renderWidth = Width * renderScale;
            int renderHeight = Height * renderScale;
            using (var chrome = new Bitmap(renderWidth, renderHeight, PixelFormat.Format32bppPArgb))
            using (Graphics chromeGraphics = Graphics.FromImage(chrome))
            {
                chromeGraphics.Clear(Color.Transparent);
                chromeGraphics.SmoothingMode = SmoothingMode.AntiAlias;
                chromeGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                chromeGraphics.CompositingQuality = CompositingQuality.HighQuality;

                Rectangle outerRect = new Rectangle(0, 0, renderWidth - 1, renderHeight - 1);
                int inset = borderWidth * renderScale;
                Rectangle innerRect = new Rectangle(
                    inset,
                    inset,
                    Math.Max(1, renderWidth - (inset * 2) - 1),
                    Math.Max(1, renderHeight - (inset * 2) - 1));
                using (GraphicsPath outerPath = Shape.Rounded(outerRect, Radius * renderScale))
                using (GraphicsPath innerPath = Shape.Rounded(innerRect, Math.Max(1, (Radius - borderWidth) * renderScale)))
                using (var borderBrush = new SolidBrush(BorderColor))
                using (var fillBrush = new SolidBrush(FillColor))
                {
                    chromeGraphics.FillPath(borderBrush, outerPath);
                    chromeGraphics.FillPath(fillBrush, innerPath);
                }

                e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
                e.Graphics.CompositingMode = CompositingMode.SourceOver;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.DrawImage(
                    chrome,
                    new Rectangle(0, 0, Width, Height),
                    new Rectangle(0, 0, renderWidth, renderHeight),
                    GraphicsUnit.Pixel);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _backdropImage != null)
            {
                _backdropImage.Dispose();
                _backdropImage = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class PopupDismissMessageFilter : IMessageFilter
    {
        private const int WmKeyDown = 0x0100;
        private const int WmLeftButtonDown = 0x0201;
        private const int WmRightButtonDown = 0x0204;
        private const int WmMiddleButtonDown = 0x0207;
        private const int WmNonClientLeftButtonDown = 0x00A1;

        private readonly Control _popup;
        private readonly Control _anchor;
        private readonly Action _dismiss;

        public PopupDismissMessageFilter(Control popup, Control anchor, Action dismiss)
        {
            _popup = popup;
            _anchor = anchor;
            _dismiss = dismiss;
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (_popup == null || !_popup.Visible || _popup.IsDisposed)
            {
                return false;
            }

            if (message.Msg == WmKeyDown && (Keys)(int)message.WParam == Keys.Escape)
            {
                _dismiss();
                return true;
            }

            if (message.Msg != WmLeftButtonDown &&
                message.Msg != WmRightButtonDown &&
                message.Msg != WmMiddleButtonDown &&
                message.Msg != WmNonClientLeftButtonDown)
            {
                return false;
            }

            Point screenPoint = Control.MousePosition;
            bool insidePopup = _popup.RectangleToScreen(_popup.ClientRectangle).Contains(screenPoint);
            bool insideAnchor = _anchor != null && !_anchor.IsDisposed &&
                                _anchor.RectangleToScreen(_anchor.ClientRectangle).Contains(screenPoint);
            if (!insidePopup && !insideAnchor)
            {
                _dismiss();
            }

            return false;
        }
    }

    internal sealed class GiftSourceOption : Control
    {
        private bool _checked;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                {
                    return;
                }

                _checked = value;
                Invalidate();
            }
        }

        public Color FillColor { get; set; }
        public Color CheckedFillColor { get; set; }
        public Color BorderColor { get; set; }
        public Color AccentColor { get; set; }

        public GiftSourceOption()
        {
            FillColor = Color.White;
            CheckedFillColor = Color.FromArgb(255, 229, 235);
            BorderColor = Color.FromArgb(241, 185, 198);
            AccentColor = Color.FromArgb(229, 65, 101);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                Checked = !Checked;
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle body = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Checked ? CheckedFillColor : FillColor;
            Color border = Checked ? AccentColor : BorderColor;
            using (GraphicsPath bodyPath = Shape.Rounded(body, 8))
            using (SolidBrush bodyBrush = new SolidBrush(fill))
            using (Pen bodyPen = new Pen(border))
            {
                e.Graphics.FillPath(bodyBrush, bodyPath);
                e.Graphics.DrawPath(bodyPen, bodyPath);
            }

            int boxSize = 16;
            Rectangle box = new Rectangle(9, (Height - boxSize) / 2, boxSize, boxSize);
            using (GraphicsPath boxPath = Shape.Rounded(box, 4))
            using (SolidBrush boxBrush = new SolidBrush(Checked ? AccentColor : Color.White))
            using (Pen boxPen = new Pen(Checked ? AccentColor : Color.FromArgb(166, 124, 138)))
            {
                e.Graphics.FillPath(boxBrush, boxPath);
                e.Graphics.DrawPath(boxPen, boxPath);
            }

            if (Checked)
            {
                using (var checkPen = new Pen(Color.White, 2f))
                {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(checkPen, new[]
                    {
                        new Point(box.X + 4, box.Y + 8),
                        new Point(box.X + 7, box.Y + 11),
                        new Point(box.X + 12, box.Y + 5)
                    });
                }
            }

            Rectangle textRect = new Rectangle(box.Right + 7, 0, Width - box.Right - 10, Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        }
    }

    internal sealed class EntryRowControl : UserControl
    {
        public const int RowHeight = 40;
        private const int NameHorizontalPadding = 10;

        private CollectedEntry _entry;
        private int _index;
        private readonly Color _text;
        private readonly Color _muted;
        private readonly Color _purple;
        private readonly Color _lavender;
        private readonly Color _line;
        private readonly Color _card;
        private readonly Color _editFill = Color.FromArgb(255, 249, 250);
        private readonly Color _editBorder = Color.FromArgb(242, 195, 205);

        private readonly PillLabel _indexBadge;
        private readonly RoundedPanel _editFrame;
        private readonly TextBox _nameBox;
        private readonly Label _metaLabel;
        private readonly RoundedPanel _coinFrame;
        private readonly TextBox _coinBox;
        private readonly Label _deleteLabel;
        private bool _syncingEntryValues;
        private bool _showMeta;
        private bool _showCoin;

        public event EventHandler EntryChanged;
        public event EventHandler DeleteClicked;
        public event EventHandler BlankClicked;

        public CollectedEntry Entry { get { return _entry; } }
        public bool IsEditing { get { return (_nameBox != null && _nameBox.Focused) || (_coinBox != null && _coinBox.Focused); } }

        public EntryRowControl(CollectedEntry entry, int index, Color text, Color muted, Color purple, Color lavender, Color line, Color card)
        {
            _entry = entry;
            _index = index;
            _text = text;
            _muted = muted;
            _purple = purple;
            _lavender = lavender;
            _line = line;
            _card = card;

            BackColor = _card;
            Height = RowHeight;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            _indexBadge = new PillLabel();
            _indexBadge.Text = _index.ToString();
            _indexBadge.Font = UiFont.Make(9.0f, FontStyle.Bold);
            _indexBadge.ForeColor = Color.FromArgb(136, 38, 65);
            _indexBadge.FillColor = _lavender;
            _indexBadge.BorderColor = _lavender;
            _indexBadge.Radius = 8;
            Controls.Add(_indexBadge);
            // 표시 모드에서는 행이 직접 그린다. 숨긴 컨트롤은 글꼴·영역과 편집 상태를 유지한다.
            _indexBadge.Visible = false;

            _editFrame = new RoundedPanel();
            _editFrame.Radius = 7;
            _editFrame.FillColor = _editFill;
            _editFrame.BorderColor = _editBorder;
            _editFrame.BackColor = _card;
            Controls.Add(_editFrame);
            _editFrame.Visible = false;

            _nameBox = new TextBox();
            _nameBox.BorderStyle = BorderStyle.None;
            _nameBox.BackColor = _editFill;
            _nameBox.ForeColor = _text;
            _nameBox.Font = UiFont.Make(10.5f, FontStyle.Regular);
            _nameBox.TextAlign = HorizontalAlignment.Center;
            _nameBox.Text = _entry.PinballName;
            _nameBox.TextChanged += delegate
            {
                if (_syncingEntryValues)
                {
                    return;
                }

                _entry.PinballName = _nameBox.Text;
                if (EntryChanged != null)
                {
                    EntryChanged(this, EventArgs.Empty);
                }
            };
            _nameBox.GotFocus += delegate
            {
                _editFrame.BorderColor = _purple;
                Invalidate(_editFrame.Bounds);
            };
            _nameBox.LostFocus += delegate
            {
                _editFrame.BorderColor = _editBorder;
                _nameBox.Visible = false;
                Invalidate(_editFrame.Bounds);
            };
            Controls.Add(_nameBox);
            _nameBox.Visible = false;
            _nameBox.BringToFront();

            _metaLabel = new Label();
            _metaLabel.AutoSize = false;
            _metaLabel.BackColor = _card;
            _metaLabel.ForeColor = Color.FromArgb(105, 68, 81);
            _metaLabel.Font = UiFont.Make(8.5f, FontStyle.Regular);
            _metaLabel.TextAlign = ContentAlignment.MiddleLeft;
            _metaLabel.Text = _entry.Nickname + " · " + GiftSourceInfo.GetLabel(_entry.Source) + " " + _entry.BalloonCount + "개";
            Controls.Add(_metaLabel);
            _metaLabel.Visible = false;

            _coinFrame = new RoundedPanel();
            _coinFrame.Radius = 7;
            _coinFrame.FillColor = _lavender;
            _coinFrame.BorderColor = Color.FromArgb(238, 150, 170);
            _coinFrame.BackColor = _card;
            _coinFrame.Cursor = Cursors.IBeam;
            Controls.Add(_coinFrame);
            _coinFrame.Visible = false;

            _coinBox = new TextBox();
            _coinBox.BorderStyle = BorderStyle.None;
            _coinBox.BackColor = _lavender;
            _coinBox.ForeColor = Color.FromArgb(142, 44, 70);
            _coinBox.Font = UiFont.Make(9.5f, FontStyle.Bold);
            _coinBox.TextAlign = HorizontalAlignment.Center;
            _coinBox.Text = GetCoinDisplayText();
            _coinBox.TextChanged += delegate
            {
                if (_syncingEntryValues)
                {
                    return;
                }

                int coins;
                if (Int32.TryParse(_coinBox.Text.Trim(), out coins) && coins > 0)
                {
                    if (coins > 9999)
                    {
                        coins = 9999;
                        _syncingEntryValues = true;
                        _coinBox.Text = coins.ToString();
                        _coinBox.SelectionStart = _coinBox.TextLength;
                        _syncingEntryValues = false;
                    }

                    _entry.CoinCount = coins;
                    LayoutChildren();
                    if (EntryChanged != null)
                    {
                        EntryChanged(this, EventArgs.Empty);
                    }
                }
            };
            _coinBox.KeyPress += delegate(object sender, KeyPressEventArgs e)
            {
                if (!Char.IsControl(e.KeyChar) && !Char.IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                }
            };
            _coinBox.GotFocus += delegate
            {
                _syncingEntryValues = true;
                _coinBox.Text = _entry.CoinCount.ToString();
                _syncingEntryValues = false;
                _coinFrame.BorderColor = _purple;
                Invalidate(_coinFrame.Bounds);
                MoveCoinCursorToEnd();
            };
            _coinBox.MouseUp += delegate
            {
                MoveCoinCursorToEnd();
            };
            _coinBox.LostFocus += delegate
            {
                if (_coinBox.Text.Trim().Length == 0 || _entry.CoinCount <= 0)
                {
                    _entry.CoinCount = 1;
                }

                _syncingEntryValues = true;
                _coinBox.Text = GetCoinDisplayText();
                _syncingEntryValues = false;
                _coinFrame.BorderColor = Color.FromArgb(238, 150, 170);
                _coinBox.Visible = false;
                Invalidate(_coinFrame.Bounds);
                if (EntryChanged != null)
                {
                    EntryChanged(this, EventArgs.Empty);
                }
            };
            Controls.Add(_coinBox);
            _coinBox.Visible = false;
            _coinBox.BringToFront();

            _deleteLabel = new Label();
            _deleteLabel.Text = "×";
            _deleteLabel.AutoSize = false;
            _deleteLabel.TextAlign = ContentAlignment.MiddleCenter;
            _deleteLabel.BackColor = _card;
            _deleteLabel.ForeColor = _muted;
            _deleteLabel.Font = UiFont.Make(9.2f, FontStyle.Regular);
            _deleteLabel.Cursor = Cursors.Hand;
            _deleteLabel.Click += delegate
            {
                if (DeleteClicked != null)
                {
                    DeleteClicked(this, EventArgs.Empty);
                }
            };
            Controls.Add(_deleteLabel);
            _deleteLabel.Visible = false;

            Resize += delegate { LayoutChildren(); };
            LayoutChildren();
        }

        private void RaiseBlankClicked(object sender, MouseEventArgs e)
        {
            if (BlankClicked != null)
            {
                BlankClicked(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _deleteLabel.Bounds.Contains(e.Location))
            {
                if (DeleteClicked != null)
                {
                    DeleteClicked(this, EventArgs.Empty);
                }
                return;
            }

            if (e.Button == MouseButtons.Left && _editFrame.Bounds.Contains(e.Location))
            {
                _coinBox.Visible = false;
                _nameBox.Visible = true;
                _nameBox.BringToFront();
                _nameBox.Focus();
                _nameBox.SelectionStart = _nameBox.TextLength;
                return;
            }

            if (e.Button == MouseButtons.Left && _showCoin && _coinFrame.Bounds.Contains(e.Location))
            {
                _nameBox.Visible = false;
                _coinBox.Visible = true;
                _coinBox.BringToFront();
                _coinBox.Focus();
                MoveCoinCursorToEnd();
                return;
            }

            RaiseBlankClicked(this, e);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor next = Cursors.Default;
            if (_deleteLabel.Bounds.Contains(e.Location))
            {
                next = Cursors.Hand;
            }
            else if (_editFrame.Bounds.Contains(e.Location) || (_showCoin && _coinFrame.Bounds.Contains(e.Location)))
            {
                next = Cursors.IBeam;
            }

            if (Cursor != next)
            {
                Cursor = next;
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        public void SetIndex(int index)
        {
            if (_index == index)
            {
                return;
            }

            _index = index;
            _indexBadge.Text = _index.ToString();
            Invalidate(_indexBadge.Bounds);
        }

        public void SetEntry(CollectedEntry entry, int index)
        {
            if (entry == null)
            {
                return;
            }

            if (Object.ReferenceEquals(_entry, entry))
            {
                SetIndex(index);
                return;
            }

            _entry = entry;
            _index = index;
            _syncingEntryValues = true;
            try
            {
                _indexBadge.Text = _index.ToString();
                _nameBox.Text = _entry.PinballName;
                _metaLabel.Text = _entry.Nickname + " · " + GiftSourceInfo.GetLabel(_entry.Source) + " " + _entry.BalloonCount + "개";
                _coinBox.Text = GetCoinDisplayText();
            }
            finally
            {
                _syncingEntryValues = false;
            }

            LayoutChildren();
            Invalidate();
        }

        public void RefreshEntryValues()
        {
            _metaLabel.Text = _entry.Nickname + " · " + GiftSourceInfo.GetLabel(_entry.Source) + " " + _entry.BalloonCount + "개";
            if (!_nameBox.Focused && _nameBox.Text != _entry.PinballName)
            {
                _syncingEntryValues = true;
                _nameBox.Text = _entry.PinballName;
                _syncingEntryValues = false;
            }
            if (!_coinBox.Focused && _coinBox.Text != GetCoinDisplayText())
            {
                _syncingEntryValues = true;
                _coinBox.Text = GetCoinDisplayText();
                _syncingEntryValues = false;
            }
            LayoutChildren();
            Invalidate();
        }

        private void LayoutChildren()
        {
            int badge = 22;
            int left = 0;
            int editX = left + badge + 8;
            int deleteW = 28;
            int coinW = _entry.CoinCount >= 1000 ? 80 : (_entry.CoinCount >= 100 ? 74 : 68);
            int coinX = Width - deleteW - coinW - 10;
            bool showMeta = Width >= 470;
            int metaW = showMeta ? Math.Min(220, Math.Max(160, Width / 5)) : 0;
            int metaX = coinX - metaW - 8;
            int editW = Math.Max(150, metaX - editX - 10);
            if (!showMeta)
            {
                editW = Math.Max(110, coinX - editX - 10);
            }

            _indexBadge.SetBounds(left, (RowHeight - badge) / 2, badge, badge);
            _deleteLabel.SetBounds(Width - deleteW - 4, (RowHeight - 26) / 2, deleteW, 26);
            const int controlY = 5;
            _editFrame.SetBounds(editX, controlY, editW, 30);
            _nameBox.SetBounds(
                editX + NameHorizontalPadding,
                controlY + Math.Max(0, (30 - _nameBox.PreferredHeight) / 2),
                Math.Max(40, editW - (NameHorizontalPadding * 2)),
                _nameBox.PreferredHeight);
            _metaLabel.SetBounds(metaX, controlY, metaW, 30);
            _coinFrame.SetBounds(coinX, controlY, coinW, 30);
            _coinBox.SetBounds(coinX + 5, controlY + Math.Max(0, (30 - _coinBox.PreferredHeight) / 2), Math.Max(18, coinW - 10), _coinBox.PreferredHeight);
            _showMeta = showMeta;
            _showCoin = Width >= 330;
            if (!_showCoin)
            {
                _coinBox.Visible = false;
            }
        }

        private string GetCoinDisplayText()
        {
            return _entry.CoinCount.ToString() + " 코인";
        }

        private void MoveCoinCursorToEnd()
        {
            if (_coinBox == null)
            {
                return;
            }

            Action move = delegate
            {
                if (!_coinBox.IsDisposed)
                {
                    _coinBox.SelectionStart = _coinBox.TextLength;
                    _coinBox.SelectionLength = 0;
                }
            };

            if (IsHandleCreated)
            {
                BeginInvoke(move);
            }
            else
            {
                move();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(_card))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle badgeBounds = new Rectangle(
                _indexBadge.Left,
                _indexBadge.Top,
                Math.Max(1, _indexBadge.Width - 1),
                Math.Max(1, _indexBadge.Height - 1));
            using (GraphicsPath badgePath = Shape.Rounded(badgeBounds, _indexBadge.Radius))
            using (var badgeBrush = new SolidBrush(_indexBadge.FillColor))
            {
                e.Graphics.FillPath(badgeBrush, badgePath);
            }
            TextRenderer.DrawText(
                e.Graphics,
                _index.ToString(),
                _indexBadge.Font,
                _indexBadge.Bounds,
                _indexBadge.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            Rectangle editBounds = new Rectangle(
                _editFrame.Left,
                _editFrame.Top,
                Math.Max(1, _editFrame.Width - 1),
                Math.Max(1, _editFrame.Height - 1));
            using (GraphicsPath editPath = Shape.Rounded(editBounds, _editFrame.Radius))
            using (var editBrush = new SolidBrush(_editFill))
            using (var editPen = new Pen(_editFrame.BorderColor))
            {
                e.Graphics.FillPath(editBrush, editPath);
                e.Graphics.DrawPath(editPen, editPath);
            }
            if (!_nameBox.Visible)
            {
                Rectangle nameBounds = _editFrame.Bounds;
                nameBounds.Inflate(-NameHorizontalPadding, 0);
                TextRenderer.DrawText(
                    e.Graphics,
                    _entry.PinballName,
                    _nameBox.Font,
                    nameBounds,
                    _nameBox.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            if (_showMeta)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    _entry.Nickname + " · " + GiftSourceInfo.GetLabel(_entry.Source) + " " + _entry.BalloonCount + "개",
                    _metaLabel.Font,
                    _metaLabel.Bounds,
                    _metaLabel.ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            if (_showCoin)
            {
                Rectangle coinBounds = new Rectangle(
                    _coinFrame.Left,
                    _coinFrame.Top,
                    Math.Max(1, _coinFrame.Width - 1),
                    Math.Max(1, _coinFrame.Height - 1));
                using (GraphicsPath coinPath = Shape.Rounded(coinBounds, _coinFrame.Radius))
                using (var coinBrush = new SolidBrush(_lavender))
                using (var coinPen = new Pen(_coinFrame.BorderColor))
                {
                    e.Graphics.FillPath(coinBrush, coinPath);
                    e.Graphics.DrawPath(coinPen, coinPath);
                }
                if (!_coinBox.Visible)
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        GetCoinDisplayText(),
                        _coinBox.Font,
                        _coinFrame.Bounds,
                        _coinBox.ForeColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                "×",
                _deleteLabel.Font,
                _deleteLabel.Bounds,
                _deleteLabel.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            using (var pen = new Pen(_line))
            {
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    internal sealed class InputRequiredDialog : Form
    {
        private readonly Color _line;
        private readonly Color _surface;

        public InputRequiredDialog(Font baseFont, Color text, Color muted, Color purple, Color line)
        {
            _line = line;
            _surface = Color.FromArgb(255, 249, 250);

            Text = "입력 필요";
            ClientSize = new Size(358, 164);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = _surface;
            Font = baseFont;
            Padding = new Padding(1);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            var close = new Label();
            close.Text = "×";
            close.AutoSize = false;
            close.TextAlign = ContentAlignment.MiddleCenter;
            close.ForeColor = muted;
            close.Font = UiFont.Make(11f, FontStyle.Regular);
            close.Cursor = Cursors.Hand;
            close.SetBounds(ClientSize.Width - 42, 16, 24, 24);
            close.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(close);

            var title = new Label();
            title.Text = "입력 필요";
            title.AutoSize = false;
            title.BackColor = Color.Transparent;
            title.ForeColor = text;
            title.Font = UiFont.Make(13.2f, FontStyle.Bold);
            title.SetBounds(24, 20, 250, 28);
            Controls.Add(title);

            var info = new PillLabel();
            info.Text = "i";
            info.TextAlign = ContentAlignment.MiddleCenter;
            info.Font = UiFont.Make(12f, FontStyle.Bold);
            info.ForeColor = Color.White;
            info.FillColor = purple;
            info.BorderColor = purple;
            info.Radius = 19;
            info.SetBounds(24, 62, 38, 38);
            Controls.Add(info);

            var body = new Label();
            body.Text = "SOOP 방송 주소 또는 SOOP ID를\r\n입력해 주세요.";
            body.AutoSize = false;
            body.BackColor = Color.Transparent;
            body.ForeColor = muted;
            body.Font = UiFont.Make(9.1f, FontStyle.Regular);
            body.TextAlign = ContentAlignment.MiddleLeft;
            body.SetBounds(76, 60, 256, 44);
            Controls.Add(body);

            var confirm = DialogButton("확인", Color.White, purple, purple);
            confirm.SetBounds(252, 112, 82, 36);
            confirm.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(confirm);

            Resize += delegate { ApplyWindowRegion(); };
            ApplyWindowRegion();
        }

        private RoundButton DialogButton(string text, Color foreColor, Color fillColor, Color borderColor)
        {
            var button = new RoundButton();
            button.Text = text;
            button.ForeColor = foreColor;
            button.FillColor = fillColor;
            button.BorderColor = borderColor;
            button.CanvasColor = _surface;
            button.Radius = 11;
            button.Font = UiFont.Make(9.2f, FontStyle.Bold);
            return button;
        }

        private void ApplyWindowRegion()
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            using (GraphicsPath path = Shape.Rounded(new Rectangle(0, 0, Width, Height), 18))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Escape)
            {
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Shape.Rounded(rect, 18))
            using (SolidBrush brush = new SolidBrush(_surface))
            using (Pen pen = new Pen(_line))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class ClearEntriesDialog : Form
    {
        private readonly Color _line;
        private readonly Color _surface;

        public ClearEntriesDialog(Font baseFont, Color text, Color muted, Color purple, Color line)
        {
            _line = line;
            _surface = Color.FromArgb(255, 240, 243);

            Text = "목록 비우기";
            ClientSize = new Size(244, 132);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = _surface;
            Font = baseFont;
            Padding = new Padding(1);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            var close = new Label();
            close.Text = "×";
            close.AutoSize = false;
            close.TextAlign = ContentAlignment.MiddleCenter;
            close.ForeColor = muted;
            close.Font = UiFont.Make(11f, FontStyle.Regular);
            close.Cursor = Cursors.Hand;
            close.SetBounds(ClientSize.Width - 34, 10, 24, 24);
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);

            var title = new Label();
            title.Text = "수집 목록을 비울까요?";
            title.AutoSize = false;
            title.BackColor = Color.Transparent;
            title.ForeColor = text;
            title.Font = UiFont.Make(12.2f, FontStyle.Bold);
            title.SetBounds(18, 13, 180, 26);
            Controls.Add(title);

            var body = new Label();
            body.Text = "목록과 핀볼 입력값이 함께 비워집니다.";
            body.AutoSize = false;
            body.BackColor = Color.Transparent;
            body.ForeColor = Color.FromArgb(99, 67, 79);
            body.Font = UiFont.Make(8.9f, FontStyle.Regular);
            body.SetBounds(18, 40, 216, 25);
            Controls.Add(body);

            var cancel = DialogButton("취소", Color.FromArgb(101, 62, 76), Color.White, Color.FromArgb(242, 186, 199));
            cancel.SetBounds(82, 82, 68, 34);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            var clear = DialogButton("비우기", Color.White, purple, purple);
            clear.SetBounds(158, 82, 68, 34);
            clear.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(clear);

            Resize += delegate { ApplyWindowRegion(); };
            ApplyWindowRegion();
        }

        private RoundButton DialogButton(string text, Color foreColor, Color fillColor, Color borderColor)
        {
            var button = new RoundButton();
            button.Text = text;
            button.ForeColor = foreColor;
            button.FillColor = fillColor;
            button.BorderColor = borderColor;
            button.CanvasColor = _surface;
            button.Radius = 11;
            button.Font = UiFont.Make(9.2f, FontStyle.Bold);
            return button;
        }

        private void ApplyWindowRegion()
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            using (GraphicsPath path = Shape.Rounded(new Rectangle(0, 0, Width, Height), 18))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = Shape.Rounded(rect, 18))
            using (SolidBrush brush = new SolidBrush(_surface))
            using (Pen pen = new Pen(Color.FromArgb(237, 162, 179)))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class FocusSinkPanel : Panel
    {
        public FocusSinkPanel()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = false;
            BackColor = Color.Transparent;
        }
    }

    internal sealed class ToastBanner : Control
    {
        public ToastBanner()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Font = UiFont.Make(9.0f, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle shadowRect = new Rectangle(2, 3, Width - 5, Height - 5);
            Rectangle bodyRect = new Rectangle(0, 0, Width - 5, Height - 6);

            using (GraphicsPath shadowPath = Shape.Rounded(shadowRect, 12))
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(58, 4, 8, 24)))
            {
                e.Graphics.FillPath(shadow, shadowPath);
            }

            using (GraphicsPath bodyPath = Shape.Rounded(bodyRect, 12))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(60, 36, 49)))
            using (Pen border = new Pen(Color.FromArgb(235, 143, 164)))
            {
                e.Graphics.FillPath(fill, bodyPath);
                e.Graphics.DrawPath(border, bodyPath);
            }

            using (SolidBrush accent = new SolidBrush(Color.FromArgb(255, 177, 118)))
            {
                e.Graphics.FillEllipse(accent, bodyRect.X + 15, bodyRect.Y + ((bodyRect.Height - 8) / 2), 8, 8);
            }

            Rectangle textRect = new Rectangle(bodyRect.X + 40, bodyRect.Y + 1, bodyRect.Width - 56, bodyRect.Height - 2);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            DoubleBuffered = true;
        }
    }

    internal sealed class VerticalScrollPanel : Panel
    {
        private const int ScrollBarInset = 14;
        private const int ThumbWidth = 7;
        private const int MinimumThumbHeight = 32;
        private readonly Color _thumbColor = Color.FromArgb(184, 190, 200);
        private readonly Color _thumbHoverColor = Color.FromArgb(159, 169, 184);
        private readonly Color _thumbDragColor = Color.FromArgb(126, 137, 154);
        private int _contentHeight;
        private int _scrollOffset;
        private int _wheelRemainder;
        private bool _thumbHot;
        private bool _dragging;
        private int _dragStartY;
        private int _dragStartOffset;

        public event EventHandler ScrollOffsetChanged;

        public int ContentWidth
        {
            get { return Math.Max(1, ClientSize.Width - (HasScrollBar ? ScrollBarInset : 0)); }
        }

        public int ScrollOffset
        {
            get { return _scrollOffset; }
        }

        public bool HasScrollBar
        {
            get { return _contentHeight > ClientSize.Height && ClientSize.Height > 0; }
        }

        public VerticalScrollPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            DoubleBuffered = true;
            AutoScroll = false;
            TabStop = false;
        }

        public void SetContentHeight(int contentHeight)
        {
            int nextHeight = Math.Max(0, contentHeight);
            if (_contentHeight == nextHeight)
            {
                SetScrollOffset(_scrollOffset);
                return;
            }

            _contentHeight = nextHeight;
            SetScrollOffset(_scrollOffset);
            Invalidate();
        }

        public void ScrollTo(int offset)
        {
            SetScrollOffset(offset);
        }

        public void ScrollToBottom()
        {
            SetScrollOffset(MaxScrollOffset);
        }

        public void ScrollByWheel(int delta)
        {
            if (!HasScrollBar || delta == 0)
            {
                return;
            }

            _wheelRemainder += delta;
            int detents = _wheelRemainder / SystemInformation.MouseWheelScrollDelta;
            if (detents == 0)
            {
                return;
            }

            _wheelRemainder -= detents * SystemInformation.MouseWheelScrollDelta;
            SetScrollOffset(_scrollOffset - (detents * EntryRowControl.RowHeight));
        }

        private int MaxScrollOffset
        {
            get { return Math.Max(0, _contentHeight - ClientSize.Height); }
        }

        private void SetScrollOffset(int offset)
        {
            int clamped = Math.Max(0, Math.Min(MaxScrollOffset, offset));
            if (_scrollOffset == clamped)
            {
                return;
            }

            _scrollOffset = clamped;
            Invalidate(new Rectangle(Math.Max(0, Width - ScrollBarInset), 0, ScrollBarInset, Height));
            if (ScrollOffsetChanged != null)
            {
                ScrollOffsetChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            SetScrollOffset(_scrollOffset);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollByWheel(e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && HasScrollBar && e.X >= Width - ScrollBarInset)
            {
                Rectangle thumb = GetThumbBounds();
                if (thumb.Contains(e.Location))
                {
                    _dragging = true;
                    _dragStartY = e.Y;
                    _dragStartOffset = _scrollOffset;
                    Capture = true;
                }
                else
                {
                    int page = Math.Max(EntryRowControl.RowHeight, ClientSize.Height - EntryRowControl.RowHeight);
                    SetScrollOffset(_scrollOffset + (e.Y < thumb.Top ? -page : page));
                }

                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Rectangle track = GetTrackBounds();
                Rectangle thumb = GetThumbBounds();
                int travel = Math.Max(1, track.Height - thumb.Height);
                int offset = _dragStartOffset + ((e.Y - _dragStartY) * MaxScrollOffset / travel);
                SetScrollOffset(offset);
            }

            bool hot = HasScrollBar && GetThumbBounds().Contains(e.Location);
            if (_thumbHot != hot)
            {
                _thumbHot = hot;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                Invalidate();
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_dragging && _thumbHot)
            {
                _thumbHot = false;
                Invalidate();
            }

            base.OnMouseLeave(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture && _dragging)
            {
                _dragging = false;
                Invalidate();
            }

            base.OnMouseCaptureChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!HasScrollBar)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle thumb = GetThumbBounds();
            Color color = _dragging ? _thumbDragColor : (_thumbHot ? _thumbHoverColor : _thumbColor);
            using (GraphicsPath path = Shape.Rounded(thumb, 3))
            using (var brush = new SolidBrush(color))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        private Rectangle GetTrackBounds()
        {
            return new Rectangle(Math.Max(0, Width - ScrollBarInset), 4, ScrollBarInset, Math.Max(1, Height - 8));
        }

        private Rectangle GetThumbBounds()
        {
            if (!HasScrollBar)
            {
                return Rectangle.Empty;
            }

            Rectangle track = GetTrackBounds();
            int thumbHeight = Math.Min(
                track.Height,
                Math.Max(MinimumThumbHeight, (int)Math.Round(track.Height * ((double)ClientSize.Height / Math.Max(1, _contentHeight)))));
            int travel = Math.Max(0, track.Height - thumbHeight);
            int thumbY = track.Y;
            if (MaxScrollOffset > 0)
            {
                thumbY += (int)Math.Round(travel * ((double)_scrollOffset / MaxScrollOffset));
            }

            return new Rectangle(Width - ThumbWidth - 2, thumbY, ThumbWidth, thumbHeight);
        }
    }

    internal sealed class GradientSurface : Panel
    {
        public Color TopColor { get; set; }
        public Color BottomColor { get; set; }

        public GradientSurface()
        {
            TopColor = Color.White;
            BottomColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (var brush = new LinearGradientBrush(bounds, TopColor, BottomColor, LinearGradientMode.ForwardDiagonal))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            using (var dot = new SolidBrush(Color.FromArgb(24, 229, 65, 101)))
            {
                for (int x = 12; x < Width; x += 28)
                {
                    for (int y = 8; y < Height + Math.Abs(AutoScrollPosition.Y); y += 28)
                    {
                        e.Graphics.FillEllipse(dot, x, y + AutoScrollPosition.Y, 2, 2);
                    }
                }
            }
        }
    }

    internal class RoundedPanel : Panel
    {
        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }

        public RoundedPanel()
        {
            Radius = 16;
            FillColor = Color.White;
            BorderColor = Color.FromArgb(241, 185, 198);
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Shape.Rounded(rect, Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class PillLabel : Label
    {
        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }

        public PillLabel()
        {
            Radius = 12;
            FillColor = Color.White;
            BorderColor = Color.White;
            BackColor = Color.Transparent;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Shape.Rounded(rect, Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class CreditBadge : Control
    {
        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public Color MarkStartColor { get; set; }
        public Color MarkEndColor { get; set; }

        public CreditBadge()
        {
            Radius = 10;
            FillColor = Color.FromArgb(255, 231, 236);
            BorderColor = Color.FromArgb(236, 147, 166);
            MarkStartColor = Color.FromArgb(242, 101, 132);
            MarkEndColor = Color.FromArgb(255, 178, 137);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Shape.Rounded(rect, Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            int markSize = Math.Min(8, Math.Max(7, Height - 11));
            Rectangle markRect = new Rectangle(7, (Height - markSize) / 2, markSize, markSize);
            using (LinearGradientBrush markBrush = new LinearGradientBrush(markRect, MarkStartColor, MarkEndColor, LinearGradientMode.ForwardDiagonal))
            {
                e.Graphics.FillEllipse(markBrush, markRect);
            }

            using (SolidBrush shine = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
            {
                e.Graphics.FillEllipse(shine, markRect.X + 2, markRect.Y + 2, 2, 2);
            }

            Rectangle textRect = new Rectangle(markRect.Right + 4, -1, Width - markRect.Right - 5, Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class GradientBadge : Control
    {
        public Color StartColor { get; set; }
        public Color EndColor { get; set; }

        public GradientBadge()
        {
            StartColor = Color.White;
            EndColor = Color.White;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Shape.Rounded(rect, 12))
            using (LinearGradientBrush brush = new LinearGradientBrush(rect, StartColor, EndColor, LinearGradientMode.ForwardDiagonal))
            {
                e.Graphics.FillPath(brush, path);
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class ImageBadge : Control
    {
        public Image Image { get; set; }
        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }

        public ImageBadge()
        {
            Radius = 12;
            FillColor = Color.White;
            BorderColor = Color.FromArgb(241, 185, 198);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Shape.Rounded(rect, Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(brush, path);

                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(path);
                if (Image != null)
                {
                    Rectangle imageRect = Cover(Image.Size, rect);
                    e.Graphics.DrawImage(Image, imageRect);
                }
                e.Graphics.Restore(state);

                e.Graphics.DrawPath(pen, path);
            }
        }

        private static Rectangle Cover(Size imageSize, Rectangle bounds)
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0)
            {
                return bounds;
            }

            float scale = Math.Max((float)bounds.Width / imageSize.Width, (float)bounds.Height / imageSize.Height);
            int width = (int)Math.Ceiling(imageSize.Width * scale);
            int height = (int)Math.Ceiling(imageSize.Height * scale);
            int x = bounds.X + ((bounds.Width - width) / 2);
            int y = bounds.Y + ((bounds.Height - height) / 2);
            return new Rectangle(x, y, width, height);
        }
    }

    internal enum ButtonGlyph
    {
        None,
        Flask,
        Trash,
        Play,
        Stop
    }

    internal sealed class RoundButton : Control
    {
        private bool _hover;
        private bool _pressed;

        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public Color GradientColor { get; set; }
        public Color CanvasColor { get; set; }
        public bool UseGradient { get; set; }
        public ButtonGlyph Glyph { get; set; }

        public RoundButton()
        {
            Radius = 10;
            FillColor = Color.White;
            BorderColor = Color.FromArgb(241, 185, 198);
            GradientColor = Color.Empty;
            CanvasColor = Color.White;
            UseGradient = false;
            Glyph = ButtonGlyph.None;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
            BackColor = CanvasColor;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            Focus();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = FillColor;
            if (_pressed)
            {
                fill = Shift(fill, -14);
            }
            else if (_hover)
            {
                fill = Shift(fill, 8);
            }

            using (SolidBrush background = new SolidBrush(CanvasColor))
            {
                e.Graphics.FillRectangle(background, ClientRectangle);
            }

            using (GraphicsPath path = Shape.Rounded(rect, Radius))
            using (Pen pen = new Pen(BorderColor))
            {
                if (UseGradient && GradientColor != Color.Empty)
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(rect, fill, GradientColor, LinearGradientMode.Horizontal))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }
                else
                {
                    using (SolidBrush brush = new SolidBrush(fill))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }

                e.Graphics.DrawPath(pen, path);
            }

            DrawCenteredButtonContent(e.Graphics, rect);
        }

        private void DrawCenteredButtonContent(Graphics graphics, Rectangle rect)
        {
            string text = Text ?? "";
            Size textSize = TextRenderer.MeasureText(
                graphics,
                text,
                Font,
                Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            int iconSize = Glyph == ButtonGlyph.None ? 0 : ((Glyph == ButtonGlyph.Play || Glyph == ButtonGlyph.Stop) ? 18 : 16);
            int gap = iconSize > 0 && text.Length > 0 ? 7 : 0;
            int contentWidth = Math.Min(rect.Width - 6, iconSize + gap + textSize.Width);
            int startX = rect.X + Math.Max(3, (rect.Width - contentWidth) / 2);

            if (iconSize > 0)
            {
                int iconOffsetY = Glyph == ButtonGlyph.Flask || Glyph == ButtonGlyph.Trash ? 1 : 0;
                DrawGlyph(graphics, new Rectangle(startX, rect.Y + ((rect.Height - iconSize) / 2) + iconOffsetY, iconSize, iconSize));
            }

            Rectangle textRect = new Rectangle(startX + iconSize + gap, rect.Y, Math.Max(1, contentWidth - iconSize - gap), rect.Height);
            TextRenderer.DrawText(
                graphics,
                text,
                Font,
                textRect,
                ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void DrawGlyph(Graphics graphics, Rectangle bounds)
        {
            Func<float, float, PointF> point = delegate(float x, float y)
            {
                return new PointF(
                    bounds.X + (x * bounds.Width / 24f),
                    bounds.Y + (y * bounds.Height / 24f));
            };

            using (var pen = new Pen(ForeColor, Math.Max(1.5f, bounds.Width * 0.095f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                if (Glyph == ButtonGlyph.Flask)
                {
                    graphics.DrawLine(pen, point(8.5f, 2f), point(15.5f, 2f));
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(point(10f, 2f), point(10f, 8.5f));
                        path.AddLine(point(10f, 8.5f), point(5.97f, 15.58f));
                        path.AddBezier(
                            point(5.97f, 15.58f),
                            point(3.94f, 19.14f),
                            point(6.42f, 21f),
                            point(9.45f, 21f));
                        path.AddLine(point(9.45f, 21f), point(14.55f, 21f));
                        path.AddBezier(
                            point(14.55f, 21f),
                            point(17.58f, 21f),
                            point(20.06f, 19.14f),
                            point(18.03f, 15.58f));
                        path.AddLine(point(18.03f, 15.58f), point(14f, 8.5f));
                        path.AddLine(point(14f, 8.5f), point(14f, 2f));
                        graphics.DrawPath(pen, path);
                    }
                    graphics.DrawLine(pen, point(7f, 16f), point(17f, 16f));
                }
                else if (Glyph == ButtonGlyph.Trash)
                {
                    graphics.DrawLine(pen, point(3f, 6f), point(21f, 6f));
                    graphics.DrawLine(pen, point(8f, 6f), point(8f, 4f));
                    graphics.DrawBezier(pen, point(8f, 4f), point(8f, 2.9f), point(8.9f, 2f), point(10f, 2f));
                    graphics.DrawLine(pen, point(10f, 2f), point(14f, 2f));
                    graphics.DrawBezier(pen, point(14f, 2f), point(15.1f, 2f), point(16f, 2.9f), point(16f, 4f));
                    graphics.DrawLine(pen, point(16f, 4f), point(16f, 6f));
                    using (var path = new GraphicsPath())
                    {
                        path.AddLine(point(19f, 6f), point(18f, 20f));
                        path.AddBezier(point(18f, 20f), point(17.92f, 21.1f), point(17.1f, 22f), point(16f, 22f));
                        path.AddLine(point(16f, 22f), point(8f, 22f));
                        path.AddBezier(point(8f, 22f), point(6.9f, 22f), point(6.08f, 21.1f), point(6f, 20f));
                        path.AddLine(point(6f, 20f), point(5f, 6f));
                        graphics.DrawPath(pen, path);
                    }
                    graphics.DrawLine(pen, point(10f, 11f), point(10f, 17f));
                    graphics.DrawLine(pen, point(14f, 11f), point(14f, 17f));
                }
                else if (Glyph == ButtonGlyph.Play)
                {
                    using (var brush = new SolidBrush(ForeColor))
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(new[] { point(5f, 3f), point(21f, 12f), point(5f, 21f) });
                        graphics.FillPath(brush, path);
                    }
                }
                else if (Glyph == ButtonGlyph.Stop)
                {
                    Rectangle stopRect = Rectangle.Round(new RectangleF(
                        point(5f, 5f).X,
                        point(5f, 5f).Y,
                        bounds.Width * 14f / 24f,
                        bounds.Height * 14f / 24f));
                    using (var brush = new SolidBrush(ForeColor))
                    using (GraphicsPath path = Shape.Rounded(stopRect, 2))
                    {
                        graphics.FillPath(brush, path);
                    }
                }
            }
        }

        private static Color Shift(Color color, int amount)
        {
            return Color.FromArgb(Clamp(color.R + amount), Clamp(color.G + amount), Clamp(color.B + amount));
        }

        private static int Clamp(int value)
        {
            return Math.Max(0, Math.Min(255, value));
        }
    }

    internal class RoundTextBox : UserControl
    {
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const int EM_GETLINECOUNT = 0x00BA;
        private const int EM_LINESCROLL = 0x00B6;
        private const int TextScrollBarInset = 17;
        private const int TextScrollThumbWidth = 7;
        private const int TextScrollThumbMinimumHeight = 32;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly TextBox _box;
        private bool _placeholderActive;
        private string _suffixText;
        private bool _textScrollThumbHot;
        private bool _textScrollDragging;
        private int _textScrollDragStartY;
        private int _textScrollDragStartLine;

        public event EventHandler InnerTextChanged;

        public string Placeholder { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public Color FocusBorderColor { get; set; }
        public Color SuffixColor { get; set; }
        public int Radius { get; set; }

        public string SuffixText
        {
            get { return _suffixText; }
            set
            {
                _suffixText = value ?? "";
                LayoutInner();
                Invalidate();
            }
        }

        public bool Multiline
        {
            get { return _box.Multiline; }
            set
            {
                _box.Multiline = value;
                _box.ScrollBars = ScrollBars.None;
                LayoutInner();
                QueueTextScrollRefresh();
            }
        }

        public override string Text
        {
            get { return _placeholderActive ? "" : _box.Text; }
            set
            {
                _placeholderActive = false;
                _box.ForeColor = Color.FromArgb(9, 17, 39);
                _box.Text = value;
                ApplyPlaceholder();
            }
        }

        public void SetTextPreserveView(string value)
        {
            value = value ?? "";
            bool hadFocus = _box.Focused;
            int firstVisibleLine = 0;
            if (_box.Multiline && _box.IsHandleCreated)
            {
                firstVisibleLine = SendMessage(_box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
            }

            int selectionStart = Math.Max(0, Math.Min(_box.SelectionStart, value.Length));
            int selectionLength = Math.Max(0, Math.Min(_box.SelectionLength, value.Length - selectionStart));
            _placeholderActive = false;
            _box.ForeColor = Color.FromArgb(9, 17, 39);
            _box.Text = value;
            _box.SelectionStart = selectionStart;
            _box.SelectionLength = selectionLength;

            if (_box.Multiline && _box.IsHandleCreated)
            {
                int currentFirstLine = SendMessage(_box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                int delta = firstVisibleLine - currentFirstLine;
                if (delta != 0)
                {
                    SendMessage(_box.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(delta));
                }
            }

            if (hadFocus)
            {
                _box.Focus();
            }
            ApplyPlaceholder();
            QueueTextScrollRefresh();
        }

        public void SetTextScrollToBottom(string value)
        {
            value = value ?? "";
            bool hadFocus = _box.Focused;
            _placeholderActive = false;
            _box.ForeColor = Color.FromArgb(9, 17, 39);
            _box.Text = value;
            if (_box.Multiline)
            {
                _box.SelectionStart = _box.TextLength;
                _box.SelectionLength = 0;
                _box.ScrollToCaret();
            }
            else
            {
                _box.SelectionStart = Math.Min(_box.TextLength, value.Length);
                _box.SelectionLength = 0;
            }
            if (hadFocus)
            {
                _box.Focus();
            }
            ApplyPlaceholder();
            QueueTextScrollRefresh();
        }

        public void AppendTextPreserveView(string value)
        {
            AppendTextCore(value, false);
        }

        public void AppendTextScrollToBottom(string value)
        {
            AppendTextCore(value, true);
        }

        private void AppendTextCore(string value, bool scrollToBottom)
        {
            value = value ?? "";
            if (value.Length == 0)
            {
                return;
            }

            bool hadFocus = _box.Focused;
            int firstVisibleLine = 0;
            if (_box.Multiline && _box.IsHandleCreated)
            {
                firstVisibleLine = SendMessage(_box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
            }

            int selectionStart = _placeholderActive ? 0 : _box.SelectionStart;
            int selectionLength = _placeholderActive ? 0 : _box.SelectionLength;
            if (_placeholderActive)
            {
                _box.Text = "";
                _placeholderActive = false;
            }

            _box.ForeColor = Color.FromArgb(9, 17, 39);
            _box.AppendText(value);
            if (scrollToBottom)
            {
                _box.SelectionStart = _box.TextLength;
                _box.SelectionLength = 0;
                if (_box.Multiline)
                {
                    _box.ScrollToCaret();
                }
            }
            else
            {
                selectionStart = Math.Max(0, Math.Min(selectionStart, _box.TextLength));
                selectionLength = Math.Max(0, Math.Min(selectionLength, _box.TextLength - selectionStart));
                _box.SelectionStart = selectionStart;
                _box.SelectionLength = selectionLength;
                if (_box.Multiline && _box.IsHandleCreated)
                {
                    int currentFirstLine = SendMessage(_box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    int delta = firstVisibleLine - currentFirstLine;
                    if (delta != 0)
                    {
                        SendMessage(_box.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(delta));
                    }
                }
            }

            if (hadFocus)
            {
                _box.Focus();
            }
            ApplyPlaceholder();
            QueueTextScrollRefresh();
        }

        public RoundTextBox() : this(9.5f)
        {
        }

        public RoundTextBox(float textSize)
        {
            Placeholder = "";
            _suffixText = "";
            FillColor = Color.FromArgb(255, 249, 250);
            BorderColor = Color.FromArgb(242, 195, 205);
            FocusBorderColor = Color.FromArgb(229, 65, 101);
            SuffixColor = Color.FromArgb(99, 67, 79);
            Radius = 10;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.BackColor = FillColor;
            _box.ForeColor = Color.FromArgb(9, 17, 39);
            _box.Font = UiFont.Make(textSize, FontStyle.Regular);
            _box.TextChanged += delegate
            {
                if (!_placeholderActive && InnerTextChanged != null)
                {
                    InnerTextChanged(this, EventArgs.Empty);
                }
                QueueTextScrollRefresh();
            };
            _box.GotFocus += delegate
            {
                if (_placeholderActive)
                {
                    _box.Text = "";
                    _box.ForeColor = Color.FromArgb(9, 17, 39);
                    _placeholderActive = false;
                }
                Invalidate();
            };
            _box.LostFocus += delegate
            {
                ApplyPlaceholder();
                Invalidate();
            };
            _box.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                int lines = SystemInformation.MouseWheelScrollLines;
                int step = lines < 0 ? Math.Max(1, GetVisibleLineCount() - 1) : Math.Max(1, lines);
                ScrollTextToLine(GetFirstVisibleLine() - (Math.Sign(e.Delta) * step));
                HandledMouseEventArgs handled = e as HandledMouseEventArgs;
                if (handled != null)
                {
                    handled.Handled = true;
                }
                QueueTextScrollRefresh();
            };
            _box.KeyUp += delegate { QueueTextScrollRefresh(); };
            _box.MouseUp += delegate { QueueTextScrollRefresh(); };
            Controls.Add(_box);

            Resize += delegate { LayoutInner(); };
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            ApplyPlaceholder();
            LayoutInner();
        }

        private void ApplyPlaceholder()
        {
            if (!Focused && !_box.Focused && _box.Text.Length == 0 && Placeholder.Length > 0)
            {
                _placeholderActive = true;
                _box.ForeColor = Color.FromArgb(99, 67, 79);
                _box.Text = Placeholder;
            }
        }

        private void LayoutInner()
        {
            int padX = 13;
            int padY = _box.Multiline ? 11 : Math.Max(6, (Height - _box.Font.Height) / 2);
            int suffixReserve = GetSuffixReserve();
            int scrollReserve = _box.Multiline ? TextScrollBarInset : 0;
            _box.BackColor = FillColor;
            _box.SetBounds(padX, padY, Math.Max(1, Width - (padX * 2) - suffixReserve - scrollReserve), Math.Max(1, Height - (padY * 2)));
            QueueTextScrollRefresh();
        }

        private int GetSuffixReserve()
        {
            if (String.IsNullOrEmpty(_suffixText))
            {
                return 0;
            }

            return Math.Max(24, TextRenderer.MeasureText(_suffixText, _box.Font).Width + 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 1 || Height <= 1)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Shape.Rounded(rect, Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
            using (Pen pen = new Pen(_box.Focused ? FocusBorderColor : BorderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            if (!String.IsNullOrEmpty(_suffixText))
            {
                int padX = 13;
                int suffixReserve = GetSuffixReserve();
                Rectangle suffixRect = new Rectangle(Width - padX - suffixReserve, 0, suffixReserve, Height - 1);
                TextRenderer.DrawText(e.Graphics, _suffixText, _box.Font, suffixRect, SuffixColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            DrawTextScrollBar(e.Graphics);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && HasTextScrollBar && e.X >= Width - TextScrollBarInset)
            {
                Rectangle thumb = GetTextScrollThumbBounds();
                if (thumb.Contains(e.Location))
                {
                    _textScrollDragging = true;
                    _textScrollDragStartY = e.Y;
                    _textScrollDragStartLine = GetFirstVisibleLine();
                    Capture = true;
                }
                else
                {
                    int page = Math.Max(1, GetVisibleLineCount() - 1);
                    ScrollTextToLine(GetFirstVisibleLine() + (e.Y < thumb.Top ? -page : page));
                }

                _box.Focus();
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_textScrollDragging)
            {
                Rectangle track = GetTextScrollTrackBounds();
                Rectangle thumb = GetTextScrollThumbBounds();
                int travel = Math.Max(1, track.Height - thumb.Height);
                int line = _textScrollDragStartLine + ((e.Y - _textScrollDragStartY) * GetMaximumFirstVisibleLine() / travel);
                ScrollTextToLine(line);
            }

            bool hot = HasTextScrollBar && GetTextScrollThumbBounds().Contains(e.Location);
            if (_textScrollThumbHot != hot)
            {
                _textScrollThumbHot = hot;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_textScrollDragging)
            {
                _textScrollDragging = false;
                Capture = false;
                Invalidate();
            }

            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_textScrollDragging && _textScrollThumbHot)
            {
                _textScrollThumbHot = false;
                Invalidate();
            }

            base.OnMouseLeave(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture && _textScrollDragging)
            {
                _textScrollDragging = false;
                Invalidate();
            }

            base.OnMouseCaptureChanged(e);
        }

        private bool HasTextScrollBar
        {
            get { return _box.Multiline && GetMaximumFirstVisibleLine() > 0; }
        }

        private int GetFirstVisibleLine()
        {
            if (!_box.IsHandleCreated)
            {
                return 0;
            }

            return Math.Max(0, SendMessage(_box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32());
        }

        private int GetTextLineCount()
        {
            if (!_box.IsHandleCreated)
            {
                return Math.Max(1, (_box.Text ?? "").Split('\n').Length);
            }

            return Math.Max(1, SendMessage(_box.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32());
        }

        private int GetVisibleLineCount()
        {
            return Math.Max(1, _box.ClientSize.Height / Math.Max(1, _box.Font.Height));
        }

        private int GetMaximumFirstVisibleLine()
        {
            return Math.Max(0, GetTextLineCount() - GetVisibleLineCount());
        }

        private void ScrollTextToLine(int line)
        {
            if (!_box.IsHandleCreated)
            {
                return;
            }

            int target = Math.Max(0, Math.Min(GetMaximumFirstVisibleLine(), line));
            int current = GetFirstVisibleLine();
            if (target != current)
            {
                SendMessage(_box.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(target - current));
            }
            Invalidate();
        }

        private Rectangle GetTextScrollTrackBounds()
        {
            int top = 10;
            return new Rectangle(Width - TextScrollThumbWidth - 5, top, TextScrollThumbWidth, Math.Max(1, Height - (top * 2)));
        }

        private Rectangle GetTextScrollThumbBounds()
        {
            if (!HasTextScrollBar)
            {
                return Rectangle.Empty;
            }

            Rectangle track = GetTextScrollTrackBounds();
            int lineCount = GetTextLineCount();
            int visibleLines = GetVisibleLineCount();
            int thumbHeight = Math.Min(
                track.Height,
                Math.Max(TextScrollThumbMinimumHeight, (int)Math.Round(track.Height * ((double)visibleLines / Math.Max(1, lineCount)))));
            int travel = Math.Max(0, track.Height - thumbHeight);
            int maximum = GetMaximumFirstVisibleLine();
            int top = track.Top;
            if (maximum > 0)
            {
                top += (int)Math.Round(travel * ((double)Math.Min(maximum, GetFirstVisibleLine()) / maximum));
            }

            return new Rectangle(track.X, top, TextScrollThumbWidth, thumbHeight);
        }

        private void DrawTextScrollBar(Graphics graphics)
        {
            if (!HasTextScrollBar)
            {
                return;
            }

            Rectangle thumb = GetTextScrollThumbBounds();
            Color color = _textScrollDragging
                ? Color.FromArgb(126, 137, 154)
                : (_textScrollThumbHot ? Color.FromArgb(159, 169, 184) : Color.FromArgb(184, 190, 200));
            using (GraphicsPath path = Shape.Rounded(thumb, 3))
            using (var brush = new SolidBrush(color))
            {
                graphics.FillPath(brush, path);
            }
        }

        private void QueueTextScrollRefresh()
        {
            Invalidate();
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            BeginInvoke((MethodInvoker)delegate
            {
                if (!IsDisposed)
                {
                    Invalidate();
                }
            });
        }
    }

    internal sealed class NumberBox : RoundTextBox
    {
        public event EventHandler ValueChanged;

        public int Minimum { get; set; }
        public int Maximum { get; set; }

        public int Value
        {
            get
            {
                int value;
                if (!Int32.TryParse(Text, out value))
                {
                    value = Minimum;
                }
                return Math.Max(Minimum, Math.Min(Maximum, value));
            }
            set
            {
                Text = Math.Max(Minimum, Math.Min(Maximum, value)).ToString();
            }
        }

        public NumberBox()
        {
            Minimum = 0;
            Maximum = 30000;
            InnerTextChanged += delegate
            {
                string cleaned = Regex.Replace(Text, "[^0-9]", "");
                if (cleaned != Text)
                {
                    Text = cleaned;
                }
                if (ValueChanged != null)
                {
                    ValueChanged(this, EventArgs.Empty);
                }
            };
        }
    }

    internal static class Shape
    {
        public static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return path;
            }

            int safeRadius = Math.Max(1, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            int diameter = Math.Max(1, safeRadius * 2);
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class PendingGift
    {
        public GiftSource Source { get; set; }
        public string Nickname { get; set; }
        public int BalloonCount { get; set; }
        public int CoinCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    internal sealed class CollectedEntry
    {
        public GiftSource Source { get; set; }
        public string Nickname { get; set; }
        public int BalloonCount { get; set; }
        public int CoinCount { get; set; }
        public string PinballName { get; set; }
        public string ReceivedAt { get; set; }
    }

    internal enum GiftSource
    {
        StarBalloon,
        AdBalloon,
        ChallengeGift
    }

    internal static class GiftSourceInfo
    {
        public static string GetLabel(GiftSource source)
        {
            if (source == GiftSource.AdBalloon)
            {
                return "애드벌룬";
            }

            if (source == GiftSource.ChallengeGift)
            {
                return "도전미션";
            }

            return "별풍선";
        }
    }
}
