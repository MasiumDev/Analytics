[CmdletBinding()]
param(
    [string]$ProjectPath = "src/backend/Analytics.Api/Analytics.Api.csproj",
    [ValidateRange(0, 300)]
    [int]$FreshnessDelaySeconds = 30,
    [uri]$GraphApiBaseUri = "https://graph.instagram.com/"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

function Get-DevelopmentToken {
    param([string]$Project)

    $prefix = "Instagram:DevelopmentAccessToken = "
    $line = & dotnet user-secrets list --project $Project 2>$null |
        Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw "Instagram:DevelopmentAccessToken is not configured in User Secrets."
    }

    $token = $line.Substring($prefix.Length)
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Instagram:DevelopmentAccessToken is empty."
    }

    return $token
}

function Get-HeaderValue {
    param(
        [System.Net.Http.HttpResponseMessage]$Response,
        [string]$Name
    )

    $values = $null
    if ($Response.Headers.TryGetValues($Name, [ref]$values)) {
        return ($values -join ",")
    }

    return $null
}

function Convert-UsageHeader {
    param(
        [string]$Value,
        [switch]$BusinessUsage
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    try {
        $document = $Value | ConvertFrom-Json
        if (-not $BusinessUsage) {
            $properties = @($document.PSObject.Properties.Name)
            $schemaRecognized =
                $properties -contains "call_count" -or
                $properties -contains "total_cputime" -or
                $properties -contains "total_time"
            return [ordered]@{
                present = $true
                schemaRecognized = $schemaRecognized
                callCountPercent = if ($schemaRecognized) { $document.call_count } else { $null }
                cpuTimePercent = if ($schemaRecognized) { $document.total_cputime } else { $null }
                totalTimePercent = if ($schemaRecognized) { $document.total_time } else { $null }
            }
        }

        $samples = @()
        foreach ($property in $document.PSObject.Properties) {
            $samples += @($property.Value)
        }
        if ($samples.Count -eq 0) {
            return [ordered]@{
                present = $true
                schemaRecognized = $false
            }
        }

        return [ordered]@{
            present = $true
            schemaRecognized = $true
            callCountPercent = ($samples | Measure-Object -Property call_count -Maximum).Maximum
            cpuTimePercent = ($samples | Measure-Object -Property total_cputime -Maximum).Maximum
            totalTimePercent = ($samples | Measure-Object -Property total_time -Maximum).Maximum
        }
    }
    catch {
        return [ordered]@{ present = $true; parseable = $false }
    }
}

function Invoke-GraphRequest {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$RelativePath
    )

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $response = $Client.GetAsync($RelativePath).GetAwaiter().GetResult()
        $content = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    }
    catch {
        $stopwatch.Stop()
        return [pscustomobject]@{
            IsSuccess = $false
            StatusCode = 0
            DurationMs = $stopwatch.ElapsedMilliseconds
            Data = $null
            BodyFingerprint = $null
            ErrorCode = $null
            ErrorSubcode = $null
            TransportError = $_.Exception.GetBaseException().GetType().Name
            AppUsage = $null
            BusinessUsage = $null
            RetryAfterSeconds = $null
        }
    }
    $stopwatch.Stop()

    $data = $null
    try {
        $data = $content | ConvertFrom-Json
    }
    catch {
        # Invalid provider JSON is reported without returning the body.
    }

    $providerErrorCode = $null
    $providerErrorSubcode = $null
    if ($null -ne $data.error) {
        $providerErrorCode = $data.error.code
        $providerErrorSubcode = $data.error.error_subcode
    }

    $result = [pscustomobject]@{
        IsSuccess = $response.IsSuccessStatusCode
        StatusCode = [int]$response.StatusCode
        DurationMs = $stopwatch.ElapsedMilliseconds
        Data = $data
        BodyFingerprint = if ([string]::IsNullOrEmpty($content)) {
            $null
        }
        else {
            $bytes = [Text.Encoding]::UTF8.GetBytes($content)
            $hash = [Security.Cryptography.SHA256]::Create()
            try {
                ([BitConverter]::ToString($hash.ComputeHash($bytes))).Replace("-", "")
            }
            finally {
                $hash.Dispose()
            }
        }
        ErrorCode = $providerErrorCode
        ErrorSubcode = $providerErrorSubcode
        TransportError = $null
        AppUsage = Convert-UsageHeader (Get-HeaderValue $response "x-app-usage")
        BusinessUsage = Convert-UsageHeader `
            (Get-HeaderValue $response "x-business-use-case-usage") `
            -BusinessUsage
        RetryAfterSeconds = Get-HeaderValue $response "Retry-After"
    }
    $response.Dispose()
    return $result
}

function New-RequestSummary {
    param([object]$Response)

    return [ordered]@{
        statusCode = $Response.StatusCode
        durationMs = $Response.DurationMs
        errorCode = $Response.ErrorCode
        errorSubcode = $Response.ErrorSubcode
        transportError = $Response.TransportError
        appUsage = $Response.AppUsage
        businessUsage = $Response.BusinessUsage
        retryAfterSeconds = $Response.RetryAfterSeconds
    }
}

function Get-MetricSummary {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Path,
        [string]$Metric,
        [AllowNull()]
        [string]$Period
    )

    $encodedMetric = [uri]::EscapeDataString($Metric)
    $relativePath = "$Path/insights?metric=$encodedMetric"
    if (-not [string]::IsNullOrWhiteSpace($Period)) {
        $encodedPeriod = [uri]::EscapeDataString($Period)
        $relativePath += "&period=$encodedPeriod"
    }
    $response = Invoke-GraphRequest $Client $relativePath
    $returnedMetric = $null
    $hasValue = $false
    $sourceEndTimeUtc = $null
    if ($response.IsSuccess -and $null -ne $response.Data.data) {
        $records = @($response.Data.data) |
            Where-Object { $null -ne $_ }
        $matchingRecord = $records |
            Where-Object { $_.name -eq $Metric } |
            Select-Object -First 1
        $returnedMetric = $matchingRecord.name
        $hasValue = $null -ne $matchingRecord -and (
            $null -ne $matchingRecord.total_value -or
            @($matchingRecord.values).Count -gt 0)
        $endTimes = @($matchingRecord.values) |
            Where-Object { $null -ne $_.end_time } |
            ForEach-Object { [DateTimeOffset]::Parse($_.end_time).ToUniversalTime() }
        if ($endTimes.Count -gt 0) {
            $sourceEndTimeUtc = ($endTimes | Sort-Object -Descending | Select-Object -First 1)
        }
    }

    return [pscustomobject]@{
        InternalResponse = $response
        Summary = [ordered]@{
            metric = $Metric
            outcome = if ($response.IsSuccess -and $returnedMetric -eq $Metric) {
                "available"
            }
            elseif ($response.IsSuccess) {
                "empty"
            }
            elseif ($response.ErrorCode -eq 100) {
                "unsupported"
            }
            else {
                "error"
            }
            valuePresent = $hasValue
            sourceEndTimeUtc = if ($null -eq $sourceEndTimeUtc) {
                $null
            }
            else {
                $sourceEndTimeUtc.ToString("O")
            }
            request = New-RequestSummary $response
        }
    }
}

$token = Get-DevelopmentToken $ProjectPath
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = $GraphApiBaseUri
$client.Timeout = [TimeSpan]::FromSeconds(30)
$client.DefaultRequestHeaders.Authorization =
    [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $token)

try {
    $profileResponse = Invoke-GraphRequest $client `
        "me?fields=user_id,account_type,media_count"
    if (-not $profileResponse.IsSuccess) {
        [ordered]@{
            runAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
            tokenConfigured = $true
            profile = New-RequestSummary $profileResponse
            completed = $false
        } | ConvertTo-Json -Depth 10
        exit 2
    }

    $mediaResponse = Invoke-GraphRequest $client `
        "me/media?fields=id,media_type,media_product_type,timestamp&limit=100"
    $media = if ($mediaResponse.IsSuccess) { @($mediaResponse.Data.data) } else { @() }
    $reel = $media |
        Where-Object { $_.media_product_type -eq "REELS" } |
        Sort-Object { [DateTimeOffset]::Parse($_.timestamp) } -Descending |
        Select-Object -First 1
    $nonReel = $media |
        Where-Object { $_.media_product_type -ne "REELS" } |
        Sort-Object { [DateTimeOffset]::Parse($_.timestamp) } -Descending |
        Select-Object -First 1

    $metricNames = @(
        "views",
        "reach",
        "likes",
        "comments",
        "saved",
        "shares",
        "total_interactions",
        "ig_reels_avg_watch_time",
        "ig_reels_video_view_total_time",
        "clips_replays_count",
        "plays",
        "skip_rate"
    )
    $samples = @()
    $freshnessCandidates = @()
    foreach ($selection in @(
        [pscustomobject]@{ Kind = "reel"; Media = $reel },
        [pscustomobject]@{ Kind = "non-reel"; Media = $nonReel }
    )) {
        if ($null -eq $selection.Media) {
            continue
        }

        $publishedAt = [DateTimeOffset]::Parse($selection.Media.timestamp).ToUniversalTime()
        $detail = Invoke-GraphRequest $client `
            "$($selection.Media.id)?fields=id,media_type,media_product_type,timestamp,like_count,comments_count"
        $availableDetailFields = @()
        if ($detail.IsSuccess) {
            foreach ($name in @("timestamp", "like_count", "comments_count")) {
                if ($detail.Data.PSObject.Properties.Name -contains $name) {
                    $availableDetailFields += $name
                }
            }
        }

        $metrics = @()
        foreach ($metric in $metricNames) {
            $probe = Get-MetricSummary $client $selection.Media.id $metric $null
            $metrics += $probe.Summary
            if ($probe.Summary.outcome -eq "available") {
                $freshnessCandidates += [pscustomobject]@{
                    Kind = $selection.Kind
                    MediaId = $selection.Media.id
                    Metric = $metric
                    FirstResponse = $probe.InternalResponse
                    FirstSourceEndTimeUtc = $probe.Summary.sourceEndTimeUtc
                }
            }
        }

        $samples += [ordered]@{
            kind = $selection.Kind
            mediaType = $selection.Media.media_type
            mediaProductType = $selection.Media.media_product_type
            publishedAgeHours = [Math]::Round(
                ([DateTimeOffset]::UtcNow - $publishedAt).TotalHours,
                2)
            detailFields = $availableDetailFields
            detailRequest = New-RequestSummary $detail
            metrics = $metrics
        }
    }

    $accountMetricNames = @(
        "views",
        "reach",
        "follower_count",
        "profile_views",
        "website_clicks",
        "accounts_engaged",
        "total_interactions"
    )
    $accountMetrics = @()
    foreach ($metric in $accountMetricNames) {
        $accountMetrics += (Get-MetricSummary $client "me" $metric "day").Summary
    }

    if ($FreshnessDelaySeconds -gt 0 -and $freshnessCandidates.Count -gt 0) {
        Start-Sleep -Seconds $FreshnessDelaySeconds
    }

    $freshness = @()
    foreach ($candidate in $freshnessCandidates) {
        $second = Get-MetricSummary `
            $client `
            $candidate.MediaId `
            $candidate.Metric `
            $null
        $freshness += [ordered]@{
            kind = $candidate.Kind
            metric = $candidate.Metric
            delaySeconds = $FreshnessDelaySeconds
            payloadChanged = $candidate.FirstResponse.BodyFingerprint -ne `
                $second.InternalResponse.BodyFingerprint
            firstSourceEndTimeUtc = $candidate.FirstSourceEndTimeUtc
            secondSourceEndTimeUtc = $second.Summary.sourceEndTimeUtc
            secondRequest = $second.Summary.request
        }
    }

    [ordered]@{
        runAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        tokenConfigured = $true
        completed = $true
        accountType = $profileResponse.Data.account_type
        profileRequest = New-RequestSummary $profileResponse
        mediaDiscovery = [ordered]@{
            request = New-RequestSummary $mediaResponse
            returnedAtLeastOneMedia = $media.Count -gt 0
            returnedAtLeastOneReel = $null -ne $reel
            returnedAtLeastOneNonReel = $null -ne $nonReel
        }
        samples = $samples
        accountMetrics = $accountMetrics
        freshness = $freshness
    } | ConvertTo-Json -Depth 20
}
finally {
    $client.Dispose()
    $handler.Dispose()
    $token = $null
}
