namespace SmartMoney.Application.Options;

public sealed class NseOptions
{
    public string ArchivesBaseUrl { get; set; } = "https://archives.nseindia.com/content/nsccl/";
    public string ParticipantOiTemplate { get; set; } = "fao_participant_oi_{ddMMyyyy}.csv";
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Base URL for FO Bhavcopy ZIP files.
    /// Full URL pattern: {FoBhavCopyBaseUrl}/{YYYY}/{MMM}/fo{DD}{MMM}{YYYY}bhav.csv.zip
    /// </summary>
    public string FoBhavCopyBaseUrl { get; set; } = "https://nsearchives.nseindia.com/content/historical/DERIVATIVES/";

    /// <summary>
    /// Full-history India VIX CSV from NSE archives.
    /// Format: Date,Open,High,Low,Close,Prev Close,Change
    /// </summary>
    public string VixArchiveUrl { get; set; } = "https://nsearchives.nseindia.com/content/indices/hist_vix_data.csv";

    /// <summary>
    /// Base URL for NSE PR (options bhavcopy) ZIP files.
    /// Full URL pattern: {PrBaseUrl}/pr{DDMMYYYY}.zip
    /// These ZIP archives contain a CSV file named pr{DDMMYYYY}.csv with options OI and volume data.
    /// Note: NSE currently hosts both pr*.zip and fo*.zip at the same base URL
    /// (https://nsearchives.nseindia.com/content/fo/), but they are kept as separate
    /// configuration properties so that each can be reconfigured independently.
    /// </summary>
    public string PrBaseUrl { get; set; } = "https://nsearchives.nseindia.com/content/fo/";

    /// <summary>
    /// Base URL for the NSE F&amp;O daily bhavcopy ZIP (fo{DDMMYYYY}.zip).
    /// Full URL pattern: {FoBhavZipBaseUrl}/fo{DDMMYYYY}.zip
    /// The ZIP contains op{DDMMYYYY}.csv with CONTRACT_D, OI_NO_CON, TRADED_QUA columns
    /// covering all strikes and expiries for index options.
    /// Note: NSE currently hosts this at the same base URL as <see cref="PrBaseUrl"/>
    /// (https://nsearchives.nseindia.com/content/fo/), but both properties are kept
    /// separate so either can be reconfigured independently if NSE changes its URLs.
    /// </summary>
    public string FoBhavZipBaseUrl { get; set; } = "https://nsearchives.nseindia.com/content/fo/";
}