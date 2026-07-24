param(
    [switch]$Apply
)

Add-Type -AssemblyName System.Data

$connectionString = 'Server=(localdb)\MSSQLLocalDB;Database=AIChatAppDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;'

function Normalize-Key([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        return ''
    }

    $builder = [System.Text.StringBuilder]::new()
    foreach ($ch in $value.ToLowerInvariant().ToCharArray()) {
        if ([char]::IsLetterOrDigit($ch)) {
            [void]$builder.Append($ch)
        }
        else {
            [void]$builder.Append(' ')
        }
    }

    return (($builder.ToString() -split '\s+' | Where-Object { $_ }) -join ' ')
}

function Read-List([object]$value) {
    if ($null -eq $value -or $value -is [DBNull] -or [string]::IsNullOrWhiteSpace([string]$value)) {
        return @()
    }

    try {
        return @(([string]$value) | ConvertFrom-Json)
    }
    catch {
        return @(([string]$value) -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
}

function Write-ListJson([string[]]$values) {
    $clean = @($values | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
    if ($clean.Count -eq 0) {
        return $null
    }

    return ($clean | ConvertTo-Json -Compress)
}

function Get-Keys($entry) {
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $titleKey = Normalize-Key $entry.Title
    if ($titleKey) {
        [void]$keys.Add($titleKey)
    }

    foreach ($alias in $entry.Aliases) {
        $key = Normalize-Key $alias
        if ($key) {
            [void]$keys.Add($key)
        }
    }

    return $keys
}

$conn = [System.Data.SqlClient.SqlConnection]::new($connectionString)
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = @'
SELECT k.Id, k.ProfileId, k.EntryType, k.SourceName, k.Title, k.Summary, k.Content, k.AliasesJson, k.KeywordsJson, k.IsPublished, k.CreatedAt, k.UpdatedAt,
       (SELECT COUNT(*) FROM ChatResponseReports r WHERE r.PromotedKnowledgeEntryId = k.Id) AS ReportLinks
FROM AssistantKnowledgeEntries k
ORDER BY k.ProfileId, k.EntryType, k.Id;
'@

$reader = $cmd.ExecuteReader()
$entries = @()
while ($reader.Read()) {
    $entry = [pscustomobject]@{
        Id = [int]$reader['Id']
        ProfileId = [string]$reader['ProfileId']
        EntryType = [string]$reader['EntryType']
        SourceName = [string]$reader['SourceName']
        Title = [string]$reader['Title']
        Summary = if ($reader['Summary'] -is [DBNull]) { $null } else { [string]$reader['Summary'] }
        Content = if ($reader['Content'] -is [DBNull]) { $null } else { [string]$reader['Content'] }
        Aliases = @(Read-List $reader['AliasesJson'])
        Keywords = @(Read-List $reader['KeywordsJson'])
        IsPublished = [bool]$reader['IsPublished']
        CreatedAt = [datetime]$reader['CreatedAt']
        UpdatedAt = [datetime]$reader['UpdatedAt']
        ReportLinks = [int]$reader['ReportLinks']
    }
    $entry | Add-Member -NotePropertyName Keys -NotePropertyValue (Get-Keys $entry)
    $entries += $entry
}
$reader.Close()

$groups = @()
foreach ($bucket in ($entries | Group-Object ProfileId, EntryType)) {
    $items = @($bucket.Group)
    $visited = @{}
    foreach ($item in $items) {
        if ($visited.ContainsKey($item.Id)) {
            continue
        }

        $component = [System.Collections.Generic.List[object]]::new()
        $queue = [System.Collections.Generic.Queue[object]]::new()
        $queue.Enqueue($item)
        $visited[$item.Id] = $true

        while ($queue.Count -gt 0) {
            $current = $queue.Dequeue()
            $component.Add($current)
            foreach ($other in $items) {
                if ($visited.ContainsKey($other.Id)) {
                    continue
                }

                $overlap = $false
                foreach ($key in $current.Keys) {
                    if ($other.Keys.Contains($key)) {
                        $overlap = $true
                        break
                    }
                }

                if ($overlap) {
                    $visited[$other.Id] = $true
                    $queue.Enqueue($other)
                }
            }
        }

        if ($component.Count -gt 1) {
            $groups += ,@($component)
        }
    }
}

if ($groups.Count -eq 0) {
    Write-Output 'No duplicate knowledge entries found.'
    $conn.Close()
    exit 0
}

Write-Output "Duplicate group count: $($groups.Count)"

$plans = @()
foreach ($group in $groups) {
    $keeper = @($group | Sort-Object @{ Expression = 'ReportLinks'; Descending = $true }, @{ Expression = 'IsPublished'; Descending = $true }, @{ Expression = 'UpdatedAt'; Descending = $true }, Id)[0]
    $duplicates = @($group | Where-Object { $_.Id -ne $keeper.Id } | Sort-Object Id)
    $plans += [pscustomobject]@{ Keeper = $keeper; Duplicates = $duplicates }

    Write-Output "KEEP #$($keeper.Id): $($keeper.Title)"
    foreach ($duplicate in $duplicates) {
        Write-Output "  REMOVE #$($duplicate.Id): $($duplicate.Title) links=$($duplicate.ReportLinks) published=$($duplicate.IsPublished)"
    }
}

if (-not $Apply) {
    Write-Output 'Dry run only. Re-run with -Apply to clean duplicates.'
    $conn.Close()
    exit 0
}

$transaction = $conn.BeginTransaction()
try {
    foreach ($plan in $plans) {
        $keeper = $plan.Keeper
        $duplicates = @($plan.Duplicates)
        $allEntries = @($keeper) + $duplicates

        $mergedAliases = @($allEntries | ForEach-Object { $_.Aliases } | ForEach-Object { $_ }) + @($allEntries | ForEach-Object { $_.Title })
        $mergedKeywords = @($allEntries | ForEach-Object { $_.Keywords } | ForEach-Object { $_ })
        $mergedAliasesJson = Write-ListJson $mergedAliases
        $mergedKeywordsJson = Write-ListJson $mergedKeywords

        $updateKeeper = $conn.CreateCommand()
        $updateKeeper.Transaction = $transaction
        $updateKeeper.CommandText = 'UPDATE AssistantKnowledgeEntries SET AliasesJson = @AliasesJson, KeywordsJson = @KeywordsJson, IsPublished = CASE WHEN @IsPublished = 1 THEN 1 ELSE IsPublished END, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = COALESCE(UpdatedBy, ''cleanup'') WHERE Id = @Id;'
        [void]$updateKeeper.Parameters.AddWithValue('@Id', $keeper.Id)
        [void]$updateKeeper.Parameters.AddWithValue('@AliasesJson', $(if ($null -eq $mergedAliasesJson) { [DBNull]::Value } else { $mergedAliasesJson }))
        [void]$updateKeeper.Parameters.AddWithValue('@KeywordsJson', $(if ($null -eq $mergedKeywordsJson) { [DBNull]::Value } else { $mergedKeywordsJson }))
        [void]$updateKeeper.Parameters.AddWithValue('@IsPublished', [int](@($allEntries | Where-Object { $_.IsPublished }).Count -gt 0))
        [void]$updateKeeper.ExecuteNonQuery()

        foreach ($duplicate in $duplicates) {
            $updateReports = $conn.CreateCommand()
            $updateReports.Transaction = $transaction
            $updateReports.CommandText = 'UPDATE ChatResponseReports SET PromotedKnowledgeEntryId = @KeeperId WHERE PromotedKnowledgeEntryId = @DuplicateId;'
            [void]$updateReports.Parameters.AddWithValue('@KeeperId', $keeper.Id)
            [void]$updateReports.Parameters.AddWithValue('@DuplicateId', $duplicate.Id)
            [void]$updateReports.ExecuteNonQuery()

            $deleteDuplicate = $conn.CreateCommand()
            $deleteDuplicate.Transaction = $transaction
            $deleteDuplicate.CommandText = 'DELETE FROM AssistantKnowledgeEntries WHERE Id = @DuplicateId;'
            [void]$deleteDuplicate.Parameters.AddWithValue('@DuplicateId', $duplicate.Id)
            [void]$deleteDuplicate.ExecuteNonQuery()
        }
    }

    $transaction.Commit()
    $removedCount = @($plans | ForEach-Object { $_.Duplicates } | ForEach-Object { $_ }).Count
    Write-Output "Cleanup complete. Removed $removedCount duplicate knowledge entr$(if ($removedCount -eq 1) { 'y' } else { 'ies' })."
}
catch {
    $transaction.Rollback()
    throw
}
finally {
    $conn.Close()
}
