namespace SmartMoney.Application.Options;

public sealed class NseOptions
{
    public string ArchivesBaseUrl { get; set; } = "https://archives.nseindia.com/content/nsccl/";
    public string ParticipantOiTemplate { get; set; } = "fao_participant_oi_{ddMMyyyy}.csv";
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Base URL for the legacy FO Bhavcopy (used as last-resort fallback only).
    /// Full URL pattern: {FoBhavCopyBaseUrl}/{YYYY}/{MMM}/fo{DD}{MMM}{YYYY}bhav.csv.zip
    /// </summary>
    public string FoBhavCopyBaseUrl { get; set; } = "https://nsearchives.nseindia.com/content/historical/DERIVATIVES/";

    /// <summary>
    /// Full-history India VIX CSV from NSE archives (fallback for VIX fetch).
    /// Format per row: Date,Open,High,Low,Close,Prev Close,Change
    /// Date format in file: DD-MMM-YYYY (e.g. 05-Mar-2026)
    /// </summary>
    public string VixArchiveUrl { get; set; } = "https://nsearchives.nseindia.com/content/indices/hist_vix_data.csv";

    /// <summary>
    /// Base URL for the NSE F&amp;O daily bhavcopy ZIP (op-bhavcopy, primary PCR source).
    /// ZIP filename pattern : fo{DDMMYYYY}.zip  (e.g. fo05032026.zip — 8-digit year)
    /// CSV inside ZIP       : op{DDMMYY}.csv    (e.g. op040326.csv  — 6-digit year, prev trading day)
    /// Source page          : https://www.nseindia.com/all-reports-derivatives
    /// Columns: CONTRACT_D, OI_NO_CON, TRADED_QUA, ...
    /// Requires NSE session cookies (prime homepage first).
    /// </summary>
    public string FoBhavZipBaseUrl { get; set; } = "https://nsearchives.nseindia.com/content/fo/";

    /// <summary>
    /// Base URL for NSE PR (options bhavcopy) ZIP files (PCR fallback source).
    /// ZIP filename pattern : PR{DDMMYY}.zip   (e.g. PR040326.zip — 6-digit year)
    /// CSV inside ZIP       : pr{DDMMYYYY}.csv (e.g. pr04032026.csv — 8-digit year)
    /// Source page          : https://www.nseindia.com/all-reports-derivatives
    /// Columns: SYMBOL, EXPIRY_DT, OPTION_TYP, STRIKE_PR, CONTRACTS, OPEN_INT, ...
    /// </summary>
    public string PrBaseUrl { get; set; } = "https://nsearchives.nseindia.com/content/fo/";
}