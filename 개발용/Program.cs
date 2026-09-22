using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("신규 프로젝트 생성기")]
[assembly: AssemblyProduct("신규 프로젝트 생성기")]
[assembly: AssemblyDescription("기존 자료를 보호하는 프로젝트 문서·소스 구조 생성기")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace NewProjectGenerator
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        private TextBox documentRootText;
        private TextBox sourceRootText;
        private NumericUpDown yearInput;
        private TextBox projectNameText;
        private ComboBox sourceModeCombo;
        private TextBox sourceNameText;
        private CheckBox designCheck;
        private CheckBox developmentCheck;
        private CheckBox manualCheck;
        private CheckBox inspectionCheck;
        private CheckBox auditCheck;
        private CheckBox catalogCheck;
        private CheckBox archiveCheck;
        private TextBox previewText;
        private CheckBox openAfterCreateCheck;
        private Label statusLabel;
        private readonly string applicationDirectory;

        public MainForm()
        {
            applicationDirectory = Application.StartupPath;
            InitializeForm();
            LoadSettings();
            UpdateSourceControls();
            UpdatePreview();
        }

        private void InitializeForm()
        {
            Text = "신규 프로젝트 생성기";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(780, 686);
            ClientSize = new Size(820, 706);
            Font = new Font("맑은 고딕", 9F, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;

            int labelX = 22;
            int inputX = 165;
            int inputWidth = 545;
            int browseX = 720;

            AddLabel("문서 기본 위치", labelX, 24);
            documentRootText = AddTextBox(inputX, 20, inputWidth, 28);
            Button documentBrowse = AddButton("찾기", browseX, 19, 76, 29);
            documentBrowse.Click += delegate { BrowseFolder(documentRootText); };

            AddLabel("소스 기본 위치", labelX, 61);
            sourceRootText = AddTextBox(inputX, 57, inputWidth, 28);
            Button sourceBrowse = AddButton("찾기", browseX, 56, 76, 29);
            sourceBrowse.Click += delegate { BrowseFolder(sourceRootText); };

            AddLabel("시작 연도", labelX, 99);
            yearInput = new NumericUpDown();
            yearInput.Location = new Point(inputX, 95);
            yearInput.Size = new Size(115, 28);
            yearInput.Minimum = 2000;
            yearInput.Maximum = 2100;
            yearInput.Value = DateTime.Now.Year;
            Controls.Add(yearInput);

            AddLabel("프로젝트명", labelX, 136);
            projectNameText = AddTextBox(inputX, 132, 631, 28);

            AddLabel("소스 생성 / 폴더명", labelX, 173);
            sourceModeCombo = new ComboBox();
            sourceModeCombo.Location = new Point(inputX, 169);
            sourceModeCombo.Size = new Size(310, 28);
            sourceModeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceModeCombo.Items.Add("생성하지 않음");
            sourceModeCombo.Items.Add("빈 소스 폴더 생성");
            sourceModeCombo.SelectedIndex = 0;
            Controls.Add(sourceModeCombo);

            sourceNameText = AddTextBox(485, 169, 311, 28);
            sourceNameText.PlaceholderTextSafe("소스 폴더명");

            GroupBox documentGroup = new GroupBox();
            documentGroup.Text = "문서 아래에 생성할 선택 폴더";
            documentGroup.Location = new Point(22, 214);
            documentGroup.Size = new Size(774, 112);
            documentGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(documentGroup);

            designCheck = AddCheckBox(documentGroup, "설계자료", 18, 28);
            developmentCheck = AddCheckBox(documentGroup, "개발자료", 135, 28);
            manualCheck = AddCheckBox(documentGroup, "사용자매뉴얼", 252, 28);
            inspectionCheck = AddCheckBox(documentGroup, "검수자료", 400, 28);
            auditCheck = AddCheckBox(documentGroup, "감리자료", 517, 28);
            catalogCheck = AddCheckBox(documentGroup, "카탈로그", 634, 28);
            archiveCheck = AddCheckBox(documentGroup, "보관자료(프로젝트 루트)", 18, 68);

            Label previewLabel = AddLabel("생성 미리보기", 22, 343);
            previewLabel.Font = new Font(Font, FontStyle.Bold);
            previewText = new TextBox();
            previewText.Location = new Point(22, 369);
            previewText.Size = new Size(774, 230);
            previewText.Multiline = true;
            previewText.ReadOnly = true;
            previewText.ScrollBars = ScrollBars.Vertical;
            previewText.BackColor = Color.White;
            previewText.Font = new Font("맑은 고딕", 9F);
            previewText.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(previewText);

            openAfterCreateCheck = new CheckBox();
            openAfterCreateCheck.Text = "생성 후 문서 프로젝트 폴더 열기";
            openAfterCreateCheck.Location = new Point(22, 617);
            openAfterCreateCheck.Size = new Size(280, 24);
            openAfterCreateCheck.Checked = true;
            openAfterCreateCheck.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(openAfterCreateCheck);

            statusLabel = new Label();
            statusLabel.Text = "기존 폴더는 덮어쓰지 않습니다.";
            statusLabel.Location = new Point(22, 647);
            statusLabel.Size = new Size(390, 24);
            statusLabel.ForeColor = Color.DimGray;
            statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(statusLabel);

            Button createButton = AddButton("프로젝트 생성", 552, 632, 140, 42);
            createButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            createButton.BackColor = Color.FromArgb(0, 120, 215);
            createButton.ForeColor = Color.White;
            createButton.FlatStyle = FlatStyle.Flat;
            createButton.Click += CreateButtonClick;

            Button closeButton = AddButton("닫기", 704, 632, 92, 42);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            closeButton.Click += delegate { Close(); };

            EventHandler changed = delegate
            {
                UpdateSourceControls();
                UpdatePreview();
            };
            documentRootText.TextChanged += changed;
            sourceRootText.TextChanged += changed;
            yearInput.ValueChanged += changed;
            projectNameText.TextChanged += changed;
            sourceModeCombo.SelectedIndexChanged += changed;
            sourceNameText.TextChanged += changed;
            designCheck.CheckedChanged += changed;
            developmentCheck.CheckedChanged += changed;
            manualCheck.CheckedChanged += changed;
            inspectionCheck.CheckedChanged += changed;
            auditCheck.CheckedChanged += changed;
            catalogCheck.CheckedChanged += changed;
            archiveCheck.CheckedChanged += changed;
        }

        private void LoadSettings()
        {
            try
            {
                AppSettings settings = AppSettings.Load(applicationDirectory);
                documentRootText.Text = settings.DocumentRoot;
                sourceRootText.Text = settings.SourceRoot;
            }
            catch (Exception ex)
            {
                documentRootText.Text = @"D:\document";
                sourceRootText.Text = @"D:\source";
                MessageBox.Show(
                    "settings.json을 읽지 못해 기본 경로를 사용합니다.\r\n\r\n" +
                    ex.Message,
                    "설정 확인",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private GenerationRequest ReadRequest()
        {
            GenerationRequest request = new GenerationRequest();
            request.ProjectName = projectNameText.Text;
            request.StartYear = Decimal.ToInt32(yearInput.Value);
            request.DocumentRoot = documentRootText.Text;
            request.SourceRoot = sourceRootText.Text;
            request.CreateSourceFolder = sourceModeCombo.SelectedIndex == 1;
            request.SourceFolderName = sourceNameText.Text;
            request.CreateArchiveFolder = archiveCheck.Checked;

            AddSelectedFolder(request.DocumentFolders, designCheck, "설계자료");
            AddSelectedFolder(request.DocumentFolders, developmentCheck, "개발자료");
            AddSelectedFolder(request.DocumentFolders, manualCheck, "사용자매뉴얼");
            AddSelectedFolder(request.DocumentFolders, inspectionCheck, "검수자료");
            AddSelectedFolder(request.DocumentFolders, auditCheck, "감리자료");
            AddSelectedFolder(request.DocumentFolders, catalogCheck, "카탈로그");
            return request;
        }

        private void UpdateSourceControls()
        {
            bool enabled = sourceModeCombo.SelectedIndex == 1;
            sourceNameText.Enabled = enabled;
            sourceRootText.Enabled = enabled;
        }

        private void UpdatePreview()
        {
            try
            {
                previewText.Text = ProjectGenerator.BuildPreview(ReadRequest());
            }
            catch (Exception ex)
            {
                previewText.Text = "미리보기를 만들 수 없습니다.\r\n" + ex.Message;
            }
        }

        private void CreateButtonClick(object sender, EventArgs e)
        {
            GenerationRequest request = ReadRequest();
            string validation = ProjectGenerator.Validate(request);
            if (validation.Length > 0)
            {
                MessageBox.Show(
                    validation,
                    "입력 확인",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "다음 구조를 새로 생성하시겠습니까?\r\n\r\n" +
                ProjectGenerator.BuildPreview(request),
                "프로젝트 생성 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            GenerationResult result;
            try
            {
                result = ProjectGenerator.Create(request);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "생성 중 문제가 발생했습니다.";
                MessageBox.Show(
                    "프로젝트를 생성하지 못했습니다.\r\n\r\n" + ex.Message,
                    "생성 실패",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string createdPaths = "문서:\r\n" + result.DocumentPath +
                (request.CreateSourceFolder
                    ? "\r\n\r\n소스:\r\n" + result.SourcePath
                    : string.Empty);
            string cleanupWarning = BuildCleanupWarning(result);

            if (openAfterCreateCheck.Checked)
            {
                FolderOpenResult openResult = ProjectFolderOpener.TryOpen(
                    result.DocumentPath,
                    delegate(string path) { Process.Start(path); });
                if (!openResult.Succeeded)
                {
                    statusLabel.Text =
                        "생성 완료 (폴더 열기 실패): " + result.DocumentPath;
                    MessageBox.Show(
                        "생성은 완료됐지만 폴더를 열지 못했습니다.\r\n\r\n" +
                        createdPaths + "\r\n\r\n열기 오류:\r\n" +
                        openResult.ErrorMessage + cleanupWarning,
                        "생성 완료 / 폴더 열기 실패",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            statusLabel.Text = "생성 완료: " + result.DocumentPath;
            MessageBox.Show(
                "프로젝트를 생성했습니다.\r\n\r\n" + createdPaths +
                cleanupWarning,
                result.CleanupWarnings.Count == 0
                    ? "생성 완료"
                    : "생성 완료 / 정리 확인 필요",
                MessageBoxButtons.OK,
                result.CleanupWarnings.Count == 0
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
        }

        private static string BuildCleanupWarning(GenerationResult result)
        {
            if (result.CleanupWarnings.Count == 0)
            {
                return string.Empty;
            }

            return "\r\n\r\n생성은 완료됐지만 작업 표시 파일을 정리하지 못했습니다." +
                "\r\n확인할 경로:\r\n" +
                string.Join("\r\n", result.CleanupWarnings.ToArray());
        }

        private void BrowseFolder(TextBox target)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "기본 폴더를 선택하세요.";
                string selectedPath = target.Text;
                try
                {
                    selectedPath = ProjectGenerator.NormalizePath(target.Text);
                }
                catch
                {
                    selectedPath = target.Text;
                }
                if (Directory.Exists(selectedPath))
                {
                    dialog.SelectedPath = selectedPath;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.SelectedPath;
                }
            }
        }

        private Label AddLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(135, 24);
            Controls.Add(label);
            return label;
        }

        private TextBox AddTextBox(int x, int y, int width, int height)
        {
            TextBox textBox = new TextBox();
            textBox.Location = new Point(x, y);
            textBox.Size = new Size(width, height);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(textBox);
            return textBox;
        }

        private Button AddButton(string text, int x, int y, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            Controls.Add(button);
            return button;
        }

        private static CheckBox AddCheckBox(
            Control parent,
            string text,
            int x,
            int y)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = text;
            checkBox.Location = new Point(x, y);
            checkBox.AutoSize = true;
            parent.Controls.Add(checkBox);
            return checkBox;
        }

        private static void AddSelectedFolder(
            List<string> folders,
            CheckBox checkBox,
            string folderName)
        {
            if (checkBox.Checked)
            {
                folders.Add(folderName);
            }
        }
    }

    internal static class TextBoxCompatibilityExtensions
    {
        public static void PlaceholderTextSafe(this TextBox textBox, string value)
        {
            // .NET Framework WinForms에는 PlaceholderText 속성이 없으므로
            // 접근성 설명으로만 안내하고 화면 배치는 별도 라벨로 보완한다.
            textBox.AccessibleDescription = value;
        }
    }
}
