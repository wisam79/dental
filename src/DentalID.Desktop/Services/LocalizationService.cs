using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace DentalID.Desktop.Services;

/// <summary>
/// Localization service with an indexer for XAML binding.
/// Supports English (LTR) and Arabic (RTL).
/// Usage in XAML: {Binding [Key], Source={x:Static services:Loc.Instance}}
/// </summary>
/// <summary>
/// Interface for localization service
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    string this[string key] { get; }
    void SwitchLanguage(string lang);
    bool IsRtl { get; }
    string CurrentLanguage { get; }
    event EventHandler<string>? LanguageChanged;
}

public class Loc : ILocalizationService, INotifyPropertyChanged
{
    private static Loc? _instance;
    private static readonly object _lock = new();
    public static Loc Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new Loc();
                }
            }
            return _instance;
        }
    }

    private readonly AppSettings _settings;
    private string _currentLanguage;

    // String tables
    private readonly Dictionary<string, Dictionary<string, string>> _strings = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? LanguageChanged;

    public bool IsRtl => _currentLanguage == "ar";
    public string CurrentLanguage => _currentLanguage;

    public Loc()
    {
        _settings = AppSettings.Load();
        _currentLanguage = _settings.Language;
        LoadStrings();
    }

    /// <summary>
    /// Indexer for XAML binding: Loc.Instance["Key"]
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(_currentLanguage, out var table) &&
                table.TryGetValue(key, out var value))
                return value;

            // Fallback to English
            if (_strings.TryGetValue("en", out var enTable) &&
                enTable.TryGetValue(key, out var enValue))
                return enValue;

            return $"[{key}]";
        }
    }

    public void SwitchLanguage(string lang)
    {
        if (_currentLanguage == lang) return;
        _currentLanguage = lang;
        _settings.Language = lang;
        _settings.Save();

        // Notify all bindings that all indexed properties changed
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        LanguageChanged?.Invoke(this, lang);
    }

    private void LoadStrings()
    {
        // ── English strings ──
        _strings["en"] = new Dictionary<string, string>
        {
            // Navigation
            ["Nav_Dashboard"] = "Dashboard",
            ["Nav_Subjects"] = "Subjects",
            ["Nav_AnalysisLab"] = "Analysis Lab",
            ["Nav_Matching"] = "Matching",
            ["Nav_Cases"] = "Cases",
            ["Nav_Reports"] = "Reports",
            ["Nav_Settings"] = "Settings",
            
            // Control
            ["Control_NoImage"] = "No image loaded",
            ["Pathology_Caries"] = "Caries",
            ["Pathology_Crown"] = "Crown",
            ["Pathology_Filling"] = "Filling",
            ["Pathology_RootCanal"] = "Root Canal",

            // App
            ["App_Title"] = "DentalID",
            ["App_TitleFull"] = "DentalID — Forensic Dental Identification",
            ["App_Subtitle"] = "Forensic Identification",
            ["App_Version"] = "v1.0.0 — Development",

            // Page Titles
            ["Page_Dashboard"] = "Dashboard",
            ["Page_Subjects"] = "Subjects",
            ["Page_AnalysisLab"] = "Analysis Lab",
            ["Page_Matching"] = "Matching Engine",
            ["Page_Cases"] = "Case Management",
            ["Page_Reports"] = "Reports",
            ["Page_Settings"] = "Settings",
            ["Page_AuditLogs"] = "Audit Logs",

            // Dashboard placeholders
            ["Dashboard_Desc"] = "Statistics and overview will appear here",
            ["Subjects_Desc"] = "Register and manage dental subjects",
            ["AnalysisLab_Desc"] = "Upload images and run AI analysis",
            ["Matching_Desc"] = "Find matches using dental fingerprints",
            ["Cases_Desc"] = "Manage forensic cases and evidence",
            ["Reports_Desc"] = "Generate forensic identification reports",
            ["Settings_Desc"] = "Application configuration and preferences",
            ["AuditLogs_Desc"] = "View system logs and verify integrity",

            // Settings
            ["Settings_Theme"] = "Theme",
            ["Settings_Language"] = "Language",
            ["Settings_ThemeDark"] = "Dark",
            ["Settings_ThemeLight"] = "Light",
            ["Settings_ThemeHC"] = "High Contrast",
            ["Settings_LangEN"] = "English",
            ["Settings_LangAR"] = "العربية",
            ["Settings_Appearance"] = "Appearance",
            ["Settings_Localization"] = "Localization",

            // Common actions
            ["Action_Save"] = "Save",
            ["Action_Cancel"] = "Cancel",
            ["Action_Delete"] = "Delete",
            ["Action_Edit"] = "Edit",
            ["Action_Add"] = "Add New",
            ["Action_Search"] = "Search",
            ["Action_Export"] = "Export",
            ["Action_Refresh"] = "Refresh",

            // Dashboard
            ["Dash_Overview"] = "Overview",
            ["Dash_TotalSubjects"] = "Total Subjects",
            ["Dash_TotalCases"] = "Total Cases",
            ["Dash_OpenCases"] = "Open / Investigation",
            ["Dash_StatusDist"] = "Case Status Distribution",
            ["Dash_RecentMatches"] = "Recent Matches",

            // Analysis Lab
            ["Lab_BrowseImage"] = "Browse Image",
            ["Lab_RunAnalysis"] = "Run Analysis",
            ["Lab_NoImage"] = "Load a dental X-ray image",
            ["Lab_Formats"] = "Supports PNG, JPG, BMP, TIFF, DICOM",
            ["Lab_Analyzing"] = "Analyzing...",
            ["Lab_Results"] = "Analysis Results",
            ["Lab_TeethCount"] = "Teeth",
            ["Lab_PathologiesCount"] = "Pathologies",
            ["Lab_EstAge"] = "Est. Age",
            ["Lab_Gender"] = "Gender",
            ["Lab_DetectedTeeth"] = "Detected Teeth (FDI)",
            ["Lab_DetectedPathologies"] = "Detected Pathologies",
            ["Lab_Tooth"] = "Tooth",
            ["Lab_SaveTitle"] = "Save Results",
            ["Lab_SelectSubject"] = "Select a subject...",
            ["Lab_SaveToSubject"] = "Save to Subject",
            ["Lab_ExportPDF"] = "Export Forensic PDF",
            ["Lab_ExportPDF"] = "Export Forensic PDF",
            ["Lab_DeleteDetection"] = "Delete Detection",
            
            // Odontogram
            ["Odo_Title"] = "Odontogram",
            ["Odo_Reset"] = "Reset",
            ["Odo_Healthy"] = "Healthy",
            ["Odo_Pathology"] = "Pathology",
            ["Odo_Palette"] = "Treatment Palette",
            ["Odo_DragHint"] = "Drag items to teeth",

            // Import Wizard
            ["Import_Title"] = "Data Import Wizard",
            ["Import_Step1"] = "Step 1: Select Source File",
            ["Import_Formats"] = "Supported Formats: CSV (Comma Separated Values)",
            ["Import_Browse"] = "Browse File...",
            ["Import_Step2"] = "Step 2: Map Columns",
            ["Import_MapDesc"] = "Link your file columns (Source) to DentalID fields (Target).",
            ["Import_Start"] = "Start Import",
            ["Import_Step3"] = "Step 3: Import Results",
            ["Import_Another"] = "Import Another File",
            
            // Login
            ["Login_Forgot"] = "Forgot Password?",
            ["Login_Show"] = "Show",
            ["Login_Hide"] = "Hide",
            
            // Audit Log
            ["Audit_Range"] = "To",

            // Messages
            ["Msg_ValidationErr"] = "Validation Error",
            ["Msg_SubjUpdated"] = "Subject updated successfully",
            ["Msg_SubjCreated"] = "Subject created successfully",
            ["Msg_SubjDeleted"] = "Subject deleted successfully",
            ["Msg_ExportSubjTitle"] = "Export Subjects",
            ["Msg_ExportCasesTitle"] = "Export Cases",
            ["Msg_ExportSuccess"] = "Export Successful",
            ["Msg_ExportedCount"] = "Exported {0} records.",
            ["Msg_ReportSaved"] = "Saved to: {0}",
            ["Msg_ReportGenTitle"] = "Report Generated",
            ["Msg_ReportSuccess"] = "Report saved successfully",
            ["Msg_MatchReady"] = "Matching Engine Ready",
            ["Msg_SelectQuery"] = "Select Query Image",
            ["Msg_FeatExtractFail"] = "Feature Extraction Failed: {0}",
            ["Msg_MatchComplete"] = "Matching scan complete",
            ["Msg_IDConfirmed"] = "ID Confirmed successfully",
            ["Msg_NoMatches"] = "No matches found",

            // --- Analysis Lab Updates ---
            ["Lab_Title"] = "IMAGE ANALYSIS WORKSTATION",
            ["Lab_SearchPlaceholder"] = "Search Subject Database (ID, Name, Case #)...",
            ["Lab_UploadHint"] = "Drag and drop X-Ray image or click 'Load Image' to begin",
            ["Lab_DropZone"] = "Drop Image Here", 
            ["Lab_ImageInfo"] = "RAW DICOM / 24-bit Depth",
            ["Lab_Report_Title"] = "Forensic Report",
            
            // Image Enhancement
            ["Lab_Enhance_Title"] = "IMAGE ENHANCEMENT",
            ["Lab_Enhance_Bright"] = "Brightness",
            ["Lab_Enhance_Contrast"] = "Contrast",
            ["Lab_Enhance_Reset"] = "Reset Filters",

            // Bio Profile & Stats
            ["Lab_Bio_Profile"] = "BIOLOGICAL PROFILE",
            ["Lab_Stats_Teeth"] = "Teeth Detected",
            ["Lab_Stats_Flags"] = "Forensic Flags",
            ["Lab_AI_Findings"] = "AI INSIGHTS & FINDINGS",

            // Fingerprint
            ["Lab_Fingerprint_Title"] = "DIGITAL FINGERPRINT",
            ["Lab_Fingerprint_Code"] = "Dental Code",
            ["Lab_Fingerprint_Uniqueness"] = "Uniqueness",
            ["Lab_Fingerprint_Vector"] = "Biometric Vector",
            
            // Tabs
            ["Lab_Tab_Summary"] = "Summary",
            ["Lab_Tab_Chart"] = "Odontogram",
            ["Lab_Tab_Flags"] = "Alerts & Flags",
            
            // Save Dialog
            ["Lab_Save_Title"] = "Save Analysis Record",
            ["Lab_Save_Subtitle"] = "Create a new subject profile or link to existing",
            ["Lab_Save_Link"] = "LINK TO EXISTING SUBJECT",
            ["Lab_Save_LinkPlaceholder"] = "Search Subject Database...",
            ["Lab_Save_NewTitle"] = "OR CREATE NEW SUBJECT",
            ["Lab_Save_Name"] = "FULL NAME",
            ["Lab_Save_NamePlaceholder"] = "e.g. John Doe / Unidentified Male 01",
            ["Lab_Save_Gender"] = "GENDER",
            ["Lab_Save_NatID"] = "NATIONAL ID / CASE #",
            ["Lab_Save_DOB"] = "DATE OF BIRTH",
            ["Lab_Save_Btn_Cancel"] = "Cancel",
            ["Lab_Save_Btn_Confirm"] = "Save & Link",
            ["Lab_Action_Save"] = "Save & Approve Report",
            ["Lab_Save_Gender_Male"] = "Male",
            ["Lab_Save_Gender_Female"] = "Female",
            ["Lab_Save_Gender_Unknown"] = "Unknown",
            ["Lab_Save_Optional"] = "Optional",

            // ViewModel Messages
            ["Msg_LabReady"] = "Forensic Lab Ready. Load evidence to begin.",
            ["Msg_LoadSubjFail"] = "Failed to load subjects: {0}",
            ["Dialog_SelectEvidence"] = "Select Forensic Evidence",
            ["Msg_InvalidFileTitle"] = "Invalid File",
            ["Msg_InvalidFileBody"] = "Please select a valid image file (PNG, JPG, JPEG, BMP, DCM)",
            ["Msg_PreviewFail"] = "⚠️ Image preview failed: {0}",
            ["Msg_EvidenceLoaded"] = "Evidence loaded. Ready for analysis.",
            ["Msg_SelectFail"] = "Failed to select image file. Please try again.",
            ["Msg_AnalysisComplete"] = "Analysis Complete",
            ["Msg_AnalysisFailed"] = "Analysis Failed: {0}",
            ["Msg_FoundTeethPathos"] = "Found {0} teeth and {1} anomalies.",
            ["Msg_NoEvidenceTitle"] = "No Evidence",
            ["Msg_NoEvidenceBody"] = "Please load and analyze an image first.",
            ["Msg_MissingInfoTitle"] = "Missing Information",
            ["Msg_MissingInfoBody"] = "Please enter the Patient Name or select an existing subject.",
            ["Msg_LinkSuccess"] = "Evidence & Fingerprint linked to: {0}",
        };

        // ── Arabic strings ──
        _strings["ar"] = new Dictionary<string, string>
        {
            // Navigation
            ["Nav_Dashboard"] = "لوحة التحكم",
            ["Nav_Subjects"] = "المواضيع",
            ["Nav_AnalysisLab"] = "مختبر التحليل",
            ["Nav_Matching"] = "المطابقة",
            ["Nav_Cases"] = "الحالات",
            ["Nav_Reports"] = "التقارير",
            ["Nav_Settings"] = "الإعدادات",

            // Control
            ["Control_NoImage"] = "لا توجد صورة",
            ["Pathology_Caries"] = "تسوس",
            ["Pathology_Crown"] = "تاج",
            ["Pathology_Filling"] = "حشوة",
            ["Pathology_RootCanal"] = "علاج جذور",

            // App
            ["App_Title"] = "DentalID",
            ["App_TitleFull"] = "DentalID — التعرف على الهوية الجنائية",
            ["App_Subtitle"] = "التعرف على الهوية بالبصمة السنية",
            ["App_Version"] = "الإصدار 1.0.0 — تطوير",

            // Page Titles
            ["Page_Dashboard"] = "لوحة التحكم",
            ["Page_Subjects"] = "المواضيع",
            ["Page_AnalysisLab"] = "مختبر التحليل",
            ["Page_Matching"] = "محرك المطابقة",
            ["Page_Cases"] = "إدارة الحالات",
            ["Page_Reports"] = "التقارير",
            ["Page_Settings"] = "الإعدادات",
            ["Page_AuditLogs"] = "سجلات التدقيق",

            // Descriptions
            ["Dashboard_Desc"] = "الإحصائيات والنظرة العامة ستظهر هنا",
            ["Subjects_Desc"] = "تسجيل وإدارة المواضيع السنية",
            ["AnalysisLab_Desc"] = "رفع الصور وتشغيل تحليل الذكاء الاصطناعي",
            ["Matching_Desc"] = "البحث عن التطابقات باستخدام البصمة السنية",
            ["Cases_Desc"] = "إدارة الحالات الجنائية والأدلة",
            ["Reports_Desc"] = "إنشاء تقارير تحديد الهوية",
            ["Settings_Desc"] = "إعدادات التطبيق والتفضيلات",
            ["AuditLogs_Desc"] = "عرض سجلات النظام والتحقق من النزاهة",

            // Settings
            ["Settings_Theme"] = "المظهر",
            ["Settings_Language"] = "اللغة",
            ["Settings_ThemeDark"] = "داكن",
            ["Settings_ThemeLight"] = "فاتح",
            ["Settings_ThemeHC"] = "تباين عالي",
            ["Settings_LangEN"] = "English",
            ["Settings_LangAR"] = "العربية",
            ["Settings_Appearance"] = "المظهر",
            ["Settings_Localization"] = "اللغة والإقليمية",

            // Common actions
            ["Action_Save"] = "حفظ",
            ["Action_Cancel"] = "إلغاء",
            ["Action_Delete"] = "حذف",
            ["Action_Edit"] = "تعديل",
            ["Action_Add"] = "إضافة جديد",
            ["Action_Search"] = "بحث",
            ["Action_Export"] = "تصدير",
            ["Action_Refresh"] = "تحديث",

            // Dashboard
            ["Dash_Overview"] = "نظرة عامة",
            ["Dash_TotalSubjects"] = "إجمالي المواضيع",
            ["Dash_TotalCases"] = "إجمالي الحالات",
            ["Dash_OpenCases"] = "قيد التحقيق",
            ["Dash_StatusDist"] = "توزيع حالات القضايا",
            ["Dash_RecentMatches"] = "التطابقات الأخيرة",

            // Analysis Lab
            ["Lab_BrowseImage"] = "استعراض صورة",
            ["Lab_RunAnalysis"] = "تشغيل التحليل",
            ["Lab_NoImage"] = "حمل صورة أشعة سينية سنية",
            ["Lab_Formats"] = "يدعم PNG, JPG, BMP, TIFF, DICOM",
            ["Lab_Analyzing"] = "جاري التحليل...",
            ["Lab_Results"] = "نتائج التحليل",
            ["Lab_TeethCount"] = "أسنان",
            ["Lab_PathologiesCount"] = "أمراض",
            ["Lab_EstAge"] = "العمر المقدر",
            ["Lab_Gender"] = "الجنس",
            ["Lab_DetectedTeeth"] = "الأسنان المكتشفة (FDI)",
            ["Lab_DetectedPathologies"] = "الأمراض المكتشفة",
            ["Lab_Tooth"] = "سن",
            ["Lab_SaveTitle"] = "حفظ النتائج",
            ["Lab_SelectSubject"] = "اختر موضوعاً...",
            ["Lab_SaveToSubject"] = "حفظ للموضوع",
            ["Lab_ExportPDF"] = "تصدير تقرير جنائي PDF",
            ["Lab_DeleteDetection"] = "حذف الكشف",

            // Odontogram
            ["Odo_Title"] = "المخطط السني",
            ["Odo_Reset"] = "إعادة تعيين",
            ["Odo_Healthy"] = "سليم",
            ["Odo_Pathology"] = "إصابة / مرض",
            ["Odo_Palette"] = "لوحة المعالجات",
            ["Odo_DragHint"] = "اسحب العناصر إلى الأسنان",

            // Import Wizard
            ["Import_Title"] = "معالج استيراد البيانات",
            ["Import_Step1"] = "الخطوة 1: اختر الملف المصدر",
            ["Import_Formats"] = "الصيغ المدعومة: CSV (قيم مفصولة بفواصل)",
            ["Import_Browse"] = "استعراض الملفات...",
            ["Import_Step2"] = "الخطوة 2: ربط الأعمدة",
            ["Import_MapDesc"] = "ربط أعمدة الملف بحقول النظام.",
            ["Import_Start"] = "بدء الاستيراد",
            ["Import_Step3"] = "الخطوة 3: نتائج الاستيراد",
            ["Import_Another"] = "استيراد ملف آخر",
            
            // Login
            ["Login_Forgot"] = "نسيت كلمة المرور؟",
            ["Login_Show"] = "إظهار",
            ["Login_Hide"] = "إخفاء",

            // Audit Log
            ["Audit_Range"] = "إلى",

            // Messages
            ["Msg_ValidationErr"] = "خطأ في التحقق",
            ["Msg_SubjUpdated"] = "تم تحديث بيانات المريض بنجاح",
            ["Msg_SubjCreated"] = "تم تسجيل المريض بنجاح",
            ["Msg_SubjDeleted"] = "تم حذف سجل المريض",
            ["Msg_ExportSubjTitle"] = "تصدير بيانات المرضى",
            ["Msg_ExportCasesTitle"] = "تصدير بيانات القضايا",
            ["Msg_ExportSuccess"] = "تم التصدير بنجاح",
            ["Msg_ExportedCount"] = "تم تصدير {0} سجل.",
            ["Msg_ReportSaved"] = "محفوظة في: {0}",
            ["Msg_ReportGenTitle"] = "تم إنشاء التقرير",
            ["Msg_ReportSuccess"] = "تم حفظ التقرير بنجاح",
            ["Msg_MatchReady"] = "محرك المطابقة جاهز",
            ["Msg_SelectQuery"] = "اختر صورة الاستعلام",
            ["Msg_FeatExtractFail"] = "فشل استخراج الميزات: {0}",
            ["Msg_MatchComplete"] = "اكتمل مسح المطابقة",
            ["Msg_IDConfirmed"] = "تم تأكيد الهوية بنجاح",
            ["Msg_NoMatches"] = "لم يتم العثور على تطابقات",

            // --- Analysis Lab Updates (Arabic) ---
            ["Lab_Title"] = "محطة تحليل الصور",
            ["Lab_SearchPlaceholder"] = "بحث في قاعدة البيانات (الاسم، المعرف، رقم القضية)...",
            ["Lab_UploadHint"] = "اسحب وأفلت صورة الأشعة هنا أو انقر على 'تحميل صورة'",
            ["Lab_DropZone"] = "أفلت الصورة هنا",
            ["Lab_ImageInfo"] = "صورة DICOM خام / عمق 24-بت",
            ["Lab_Report_Title"] = "التقرير الجنائي",

            // Image Enhancement
            ["Lab_Enhance_Title"] = "تحسين الصورة",
            ["Lab_Enhance_Bright"] = "السطوع",
            ["Lab_Enhance_Contrast"] = "التباين",
            ["Lab_Enhance_Reset"] = "إعادة تعيين المرشحات",

            // Bio Profile & Stats
            ["Lab_Bio_Profile"] = "الملف البيولوجي",
            ["Lab_Stats_Teeth"] = "الأسنان المكتشفة",
            ["Lab_Stats_Flags"] = "تنبيهات جنائية",
            ["Lab_AI_Findings"] = "رؤى واستنتاجات الذكاء الاصطناعي",

            // Fingerprint
            ["Lab_Fingerprint_Title"] = "البصمة الرقمية",
            ["Lab_Fingerprint_Code"] = "الكود السني",
            ["Lab_Fingerprint_Uniqueness"] = "درجة التفرد",
            ["Lab_Fingerprint_Vector"] = "البصمة البيومترية",

            // Tabs
            ["Lab_Tab_Summary"] = "ملخص",
            ["Lab_Tab_Chart"] = "المخطط السني",
            ["Lab_Tab_Flags"] = "التنبيهات",

            // Save Dialog
            ["Lab_Save_Title"] = "حفظ سجل التحليل",
            ["Lab_Save_Subtitle"] = "إنشاء ملف جديد أو الربط بموضوع موجود",
            ["Lab_Save_Link"] = "ربط بموضوع موجود",
            ["Lab_Save_LinkPlaceholder"] = "بحث في قاعدة البيانات...",
            ["Lab_Save_NewTitle"] = "أو إنشاء موضوع جديد",
            ["Lab_Save_Name"] = "الاسم الكامل",
            ["Lab_Save_NamePlaceholder"] = "مثال: مجهول 01",
            ["Lab_Save_Gender"] = "الجنس",
            ["Lab_Save_NatID"] = "المعرف الوطني / رقم القضية",
            ["Lab_Save_DOB"] = "تاريخ الميلاد",
            ["Lab_Save_Btn_Cancel"] = "إلغاء",
            ["Lab_Save_Btn_Confirm"] = "حفظ وربط",
            ["Lab_Action_Save"] = "حفظ واعتماد التقرير",
            ["Lab_Save_Gender_Male"] = "ذكر",
            ["Lab_Save_Gender_Female"] = "أنثى",
            ["Lab_Save_Gender_Unknown"] = "غير معروف",
            ["Lab_Save_Optional"] = "اختياري",

            // ViewModel Messages (Arabic)
            ["Msg_LabReady"] = "المختبر الجنائي جاهز. قم بتحميل الأدلة للبدء.",
            ["Msg_LoadSubjFail"] = "فشل تحميل المواضيع: {0}",
            ["Dialog_SelectEvidence"] = "اختر الأدلة الجنائية",
            ["Msg_InvalidFileTitle"] = "ملف غير صالح",
            ["Msg_InvalidFileBody"] = "يرجى اختيار ملف صورة صالح (PNG, JPG, JPEG, BMP, DCM)",
            ["Msg_PreviewFail"] = "⚠️ فشل معاينة الصورة: {0}",
            ["Msg_EvidenceLoaded"] = "تم تحميل الدليل. جاهز للتحليل.",
            ["Msg_SelectFail"] = "فشل اختيار ملف الصورة. يرجى المحاولة مرة أخرى.",
            ["Msg_AnalysisComplete"] = "اكتمل التحليل",
            ["Msg_AnalysisFailed"] = "فشل التحليل: {0}",
            ["Msg_FoundTeethPathos"] = "تم العثور على {0} أسنان و {1} شذوذ.",
            ["Msg_NoEvidenceTitle"] = "لا توجد أدلة",
            ["Msg_NoEvidenceBody"] = "يرجى تحميل وتحليل صورة أولاً.",
            ["Msg_MissingInfoTitle"] = "معلومات ناقصة",
            ["Msg_MissingInfoBody"] = "يرجى إدخال اسم المريض أو اختيار موضوع موجود.",
            ["Msg_LinkSuccess"] = "تم ربط الدليل والبصمة بـ: {0}"
        };
    }
}


