#Requires -Version 7.0
<#
.SYNOPSIS
    サンプルアプリ (Source/Example) が使うサイトとデータを、プリザンターに作る。

.DESCRIPTION
    プリザンターの API で、次の 3 つのサイトを最上位に作り、架空のサンプルデータを入れる。
      <接頭辞>取引先 (記録テーブル)   … 会社名・業種・電話番号・備考
      <接頭辞>案件   (記録テーブル)   … 案件名・取引先 (リンク)・区分・状況・金額・受注日・確認済・メモ
      <接頭辞>作業   (期限付きテーブル) … 作業名・案件 (リンク)・担当者・状況・開始・完了・作業量・進捗率
    終わりに、接続文字列の Sites に書く値 (Sites=Customers:<ID>,Projects:<ID>,Tasks:<ID>) を表示する。

    作ったサイトには「サイト名」(SiteName) として <接頭辞>Customers / <接頭辞>Projects / <接頭辞>Tasks を付ける。
    同じサイト名のサイトが既にあれば、-Recreate を付けたときは消してから作り直し、付けないときは何も作らずに既存のサイトの ID を表示する。
    (既にあるかを調べるのに、作り始めの取引先のサイトを使う。既にあって -Recreate が無いときは、作り始めたサイトを消して終わる)

    API キーはテナントの管理者など、最上位にサイトを作れる利用者のもの。キーはこのファイルに書かず、引数かファイルで渡す。

.PARAMETER Url
    プリザンターのアプリのルート (仮想ディレクトリまで)。例: https://pleasanter.example.com/  http は localhost / 127.0.0.1 だけ。

.PARAMETER ApiKey
    API キー。

.PARAMETER ApiKeyFile
    API キーを 1 行だけ書いたファイルのパス (-ApiKey の代わり)。

.PARAMETER Recreate
    同じサイト名のサイトがあれば消してから作る (中のレコードも消える)。

.PARAMETER NamePrefix
    サイトのタイトルとサイト名の頭に付ける文字 (既定 "CLB見本_")。

.EXAMPLE
    pwsh ./setup-pleasanter.ps1 -Url http://localhost:59803/ -ApiKeyFile C:\secrets\pleasanter-api-key.txt
#>
[CmdletBinding(DefaultParameterSetName = 'KeyFile')]
param(
    [Parameter(Mandatory)] [string] $Url,
    [Parameter(Mandatory, ParameterSetName = 'Key')] [string] $ApiKey,
    [Parameter(Mandatory, ParameterSetName = 'KeyFile')] [string] $ApiKeyFile,
    [switch] $Recreate,
    [string] $NamePrefix = 'CLB見本_'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

# ---- 接続 ----

$uri = [Uri]$null
if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri)) { throw "Url が URL の形ではありません: $Url" }
if ($uri.Scheme -ne 'https' -and -not ($uri.Scheme -eq 'http' -and $uri.Host -in @('localhost', '127.0.0.1'))) {
    throw 'Url は https:// で指定してください (http:// は localhost / 127.0.0.1 だけ)。'
}
$baseUrl = $Url.TrimEnd('/') + '/'

if ($PSCmdlet.ParameterSetName -eq 'KeyFile') {
    $ApiKey = (Get-Content -LiteralPath $ApiKeyFile -Raw).Trim()
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) { throw 'API キーが空です。' }

# プリザンターの API を呼ぶ。成功 (HTTP 200 かつ StatusCode 200) なら応答の JSON を返し、それ以外は例外 (キーは入れない)
function Invoke-Pleasanter([string] $Path, [hashtable] $Body = @{}) {
    $payload = [ordered]@{ ApiVersion = 1.1; ApiKey = $ApiKey }
    foreach ($k in $Body.Keys) { $payload[$k] = $Body[$k] }
    $json = $payload | ConvertTo-Json -Depth 20 -Compress
    $response = Invoke-WebRequest -Uri ($baseUrl + $Path) -Method Post -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($json)) -SkipHttpErrorCheck -MaximumRedirection 0 -TimeoutSec 100
    $status = [int]$response.StatusCode
    $result = $null
    try { $result = $response.Content | ConvertFrom-Json } catch { }
    if ($status -ne 200 -or $null -eq $result -or $result.StatusCode -ne 200) {
        $message = if ($null -ne $result -and $result.PSObject.Properties['Message']) { $result.Message } else { '(JSON でない応答)' }
        throw "プリザンターの API が失敗しました: $Path HTTP $status $message"
    }
    return $result
}

function New-Site([string] $Title, [string] $ReferenceType, [hashtable] $SiteSettings) {
    $r = Invoke-Pleasanter 'api/items/0/createsite' @{ Title = $Title; ReferenceType = $ReferenceType; SiteSettings = $SiteSettings }
    return [long]$r.Id
}

# サイト名で探す (見つからなければ -1)。起点 (StartId) は読めるサイトでなければならない
function Find-Site([long] $StartId, [string] $SiteName) {
    $r = Invoke-Pleasanter "api/items/$StartId/GetClosestSiteId" @{ FindSiteNames = @($SiteName) }
    return [long]$r.Data[0].SiteId
}

function Remove-Site([long] $SiteId) {
    Invoke-Pleasanter "api/items/$SiteId/deletesite" | Out-Null
}

# サイト名を付ける (サイトの更新として保存されるので、リンクの列の結び付きもここで登録される)
function Set-SiteName([long] $SiteId, [string] $SiteName) {
    Invoke-Pleasanter "api/items/$SiteId/updatesite" @{ SiteName = $SiteName } | Out-Null
}

function New-Record([long] $SiteId, [hashtable] $Record) {
    $r = Invoke-Pleasanter "api/items/$SiteId/create" $Record
    return [long]$r.Id
}

# ---- サイトの定義 ----

$tables = [ordered]@{
    Customers = @{ Title = "${NamePrefix}取引先"; SiteName = "${NamePrefix}Customers" }
    Projects  = @{ Title = "${NamePrefix}案件"; SiteName = "${NamePrefix}Projects" }
    Tasks     = @{ Title = "${NamePrefix}作業"; SiteName = "${NamePrefix}Tasks" }
}

$customersSettings = @{
    Columns          = @(
        @{ ColumnName = 'Title'; LabelText = '会社名' }
        @{ ColumnName = 'ClassA'; LabelText = '業種'; ChoicesText = "製造`n物流`n食品`n建設`n情報通信" }
        @{ ColumnName = 'ClassB'; LabelText = '電話番号' }
        @{ ColumnName = 'DescriptionA'; LabelText = '備考' }
    )
    EditorColumnHash = @{ General = @('Title', 'ClassA', 'ClassB', 'DescriptionA') }
    GridColumns      = @('ResultId', 'Title', 'ClassA', 'ClassB')
}

function Get-ProjectsSettings([long] $CustomersId) {
    @{
        Columns          = @(
            @{ ColumnName = 'Title'; LabelText = '案件名' }
            @{ ColumnName = 'ClassA'; LabelText = '取引先'; ChoicesText = "[[$CustomersId]]" }
            @{ ColumnName = 'ClassB'; LabelText = '区分'; ChoicesText = "新規`n追加`n保守" }
            @{ ColumnName = 'Status'; LabelText = '状況'; ChoicesText = "100,見込み`n200,提案中`n300,受注`n900,失注" }
            @{ ColumnName = 'NumA'; LabelText = '金額'; DecimalPlaces = 0; Unit = '円' }
            @{ ColumnName = 'DateA'; LabelText = '受注日'; EditorFormat = 'Ymd' }
            @{ ColumnName = 'CheckA'; LabelText = '確認済' }
            @{ ColumnName = 'DescriptionA'; LabelText = 'メモ' }
        )
        # リンクの列は ChoicesText だけでなく Links にも書く (API で作ったサイトは ChoicesText から Links を作らない。無いとリンクとして扱われない)
        Links            = @(@{ ColumnName = 'ClassA'; SiteId = $CustomersId })
        EditorColumnHash = @{ General = @('Title', 'ClassA', 'ClassB', 'Status', 'NumA', 'DateA', 'CheckA', 'DescriptionA') }
        GridColumns      = @('ResultId', 'Title', 'ClassA', 'ClassB', 'Status', 'NumA', 'DateA', 'CheckA')
    }
}

function Get-TasksSettings([long] $ProjectsId) {
    @{
        Columns          = @(
            @{ ColumnName = 'Title'; LabelText = '作業名' }
            @{ ColumnName = 'ClassA'; LabelText = '案件'; ChoicesText = "[[$ProjectsId]]" }
            @{ ColumnName = 'ClassB'; LabelText = '担当者'; ChoicesText = "佐藤`n鈴木`n高橋`n田中" }
            @{ ColumnName = 'StartTime'; LabelText = '開始' }
            @{ ColumnName = 'CompletionTime'; LabelText = '完了' }
            @{ ColumnName = 'WorkValue'; LabelText = '作業量' }
        )
        Links            = @(@{ ColumnName = 'ClassA'; SiteId = $ProjectsId })
        EditorColumnHash = @{ General = @('Title', 'ClassA', 'ClassB', 'Status', 'StartTime', 'CompletionTime', 'WorkValue', 'ProgressRate') }
        GridColumns      = @('IssueId', 'Title', 'ClassA', 'ClassB', 'Status', 'StartTime', 'CompletionTime', 'WorkValue', 'ProgressRate')
    }
}

# ---- サンプルデータ (架空) ----

$customers = @(
    @{ Title = '見本工業株式会社'; Industry = '製造'; Phone = '03-0000-0101'; Note = '主要取引先' }
    @{ Title = '架空物流株式会社'; Industry = '物流'; Phone = '06-0000-0202'; Note = '' }
    @{ Title = 'サンプル食品株式会社'; Industry = '食品'; Phone = '052-000-0303'; Note = '月末締め' }
    @{ Title = 'テスト建設株式会社'; Industry = '建設'; Phone = '092-000-0404'; Note = '' }
    @{ Title = '例示システムズ株式会社'; Industry = '情報通信'; Phone = '011-000-0505'; Note = '紹介元: 見本工業' }
)

# Customer = $customers の添字。Status: 100 見込み / 200 提案中 / 300 受注 / 900 失注
$projects = @(
    @{ Title = '生産管理システムの更新'; Customer = 0; Kind = '追加'; Status = 300; Amount = 4800000; Ordered = '2026/04/15'; Checked = $true; Memo = '第 1 期は 9 月まで' }
    @{ Title = '倉庫の在庫照会画面'; Customer = 1; Kind = '新規'; Status = 300; Amount = 2350000; Ordered = '2026/05/20'; Checked = $true; Memo = '' }
    @{ Title = '配送ルートの見直し'; Customer = 1; Kind = '新規'; Status = 200; Amount = 1200000; Ordered = $null; Checked = $false; Memo = '見積を提出済み' }
    @{ Title = '店舗の売上集計'; Customer = 2; Kind = '新規'; Status = 100; Amount = 900000; Ordered = $null; Checked = $false; Memo = '' }
    @{ Title = '原価計算の保守'; Customer = 2; Kind = '保守'; Status = 300; Amount = 600000; Ordered = '2026/06/01'; Checked = $true; Memo = '' }
    @{ Title = '現場日報アプリ'; Customer = 3; Kind = '新規'; Status = 200; Amount = 3000000; Ordered = $null; Checked = $false; Memo = 'デモを 2 回実施' }
    @{ Title = '安全書類の電子化'; Customer = 3; Kind = '追加'; Status = 900; Amount = 1500000; Ordered = $null; Checked = $true; Memo = '予算の都合で見送り' }
    @{ Title = '社内ポータルの改修'; Customer = 4; Kind = '追加'; Status = 300; Amount = 2000000; Ordered = '2026/07/10'; Checked = $false; Memo = '' }
    @{ Title = '問い合わせ管理'; Customer = 4; Kind = '新規'; Status = 100; Amount = 1800000; Ordered = $null; Checked = $false; Memo = '' }
    @{ Title = '設備点検の記録'; Customer = 0; Kind = '新規'; Status = 200; Amount = 2700000; Ordered = $null; Checked = $false; Memo = '' }
    @{ Title = '年次保守 2026'; Customer = 0; Kind = '保守'; Status = 300; Amount = 1000000; Ordered = '2026/04/01'; Checked = $true; Memo = '' }
    @{ Title = 'EDI 連携'; Customer = 2; Kind = '追加'; Status = 100; Amount = 2200000; Ordered = $null; Checked = $false; Memo = '取引先の仕様待ち' }
)

# Project = $projects の添字。Status: 100 未着手 / 150 準備中 / 200 実施中 / 300 レビュー中 / 900 完了 / 910 保留
$tasks = @(
    @{ Title = '現行の画面の調査'; Project = 0; Person = '佐藤'; Status = 900; Start = '2026/04/20'; End = '2026/04/30'; Work = 16; Progress = 100 }
    @{ Title = 'データ移行の設計'; Project = 0; Person = '鈴木'; Status = 200; Start = '2026/05/07'; End = '2026/06/15'; Work = 40; Progress = 60 }
    @{ Title = '帳票の作成'; Project = 0; Person = '高橋'; Status = 100; Start = '2026/06/16'; End = '2026/07/31'; Work = 32; Progress = 0 }
    @{ Title = '受け入れテスト'; Project = 0; Person = '佐藤'; Status = 100; Start = '2026/08/01'; End = '2026/09/15'; Work = 24; Progress = 0 }
    @{ Title = '在庫の画面の試作'; Project = 1; Person = '田中'; Status = 900; Start = '2026/05/25'; End = '2026/06/10'; Work = 20; Progress = 100 }
    @{ Title = '在庫の照会の実装'; Project = 1; Person = '田中'; Status = 300; Start = '2026/06/11'; End = '2026/07/20'; Work = 48; Progress = 90 }
    @{ Title = '倉庫での操作説明'; Project = 1; Person = '鈴木'; Status = 150; Start = '2026/07/21'; End = '2026/07/31'; Work = 8; Progress = 10 }
    @{ Title = '配送データの分析'; Project = 2; Person = '高橋'; Status = 200; Start = '2026/06/01'; End = '2026/06/30'; Work = 24; Progress = 40 }
    @{ Title = '原価の再計算の修正'; Project = 4; Person = '佐藤'; Status = 900; Start = '2026/06/05'; End = '2026/06/12'; Work = 6; Progress = 100 }
    @{ Title = '月次の保守報告'; Project = 4; Person = '佐藤'; Status = 200; Start = '2026/07/01'; End = '2027/03/31'; Work = 12; Progress = 30 }
    @{ Title = '日報の画面のデモ'; Project = 5; Person = '鈴木'; Status = 900; Start = '2026/06/20'; End = '2026/06/25'; Work = 8; Progress = 100 }
    @{ Title = '見積の再提出'; Project = 5; Person = '田中'; Status = 910; Start = '2026/07/01'; End = '2026/07/15'; Work = 4; Progress = 0 }
    @{ Title = 'ポータルの要件の整理'; Project = 7; Person = '高橋'; Status = 900; Start = '2026/07/15'; End = '2026/07/31'; Work = 16; Progress = 100 }
    @{ Title = 'お知らせ機能の実装'; Project = 7; Person = '高橋'; Status = 200; Start = '2026/08/01'; End = '2026/09/10'; Work = 40; Progress = 50 }
    @{ Title = '権限の設定'; Project = 7; Person = '田中'; Status = 100; Start = '2026/09/11'; End = '2026/09/30'; Work = 12; Progress = 0 }
    @{ Title = '点検の項目のヒアリング'; Project = 9; Person = '鈴木'; Status = 200; Start = '2026/08/05'; End = '2026/08/20'; Work = 10; Progress = 70 }
    @{ Title = 'サーバーの更新'; Project = 10; Person = '佐藤'; Status = 900; Start = '2026/04/10'; End = '2026/04/20'; Work = 12; Progress = 100 }
    @{ Title = 'バックアップの確認'; Project = 10; Person = '田中'; Status = 300; Start = '2026/10/01'; End = '2026/10/10'; Work = 4; Progress = 80 }
    @{ Title = '障害の問い合わせ対応'; Project = 10; Person = '高橋'; Status = 150; Start = '2026/11/01'; End = '2026/11/30'; Work = 6; Progress = 20 }
    @{ Title = '連携の仕様の確認'; Project = 11; Person = '鈴木'; Status = 910; Start = '2026/09/01'; End = '2026/09/30'; Work = 8; Progress = 0 }
)

# ---- 作る ----

Write-Host "サイトを作ります ($baseUrl)"

# 取引先のサイトを作り、そこを起点に同じサイト名のサイトを探す
$customersId = New-Site $tables.Customers.Title 'Results' $customersSettings
$existing = [ordered]@{}
foreach ($name in $tables.Keys) {
    $found = Find-Site $customersId $tables[$name].SiteName
    if ($found -gt 0) { $existing[$name] = $found }
}

if ($existing.Count -gt 0 -and -not $Recreate) {
    Remove-Site $customersId
    Write-Host '同じサイト名のサイトが既にあるので、何も作りませんでした (作り直すときは -Recreate)。'
    $existing.GetEnumerator() | ForEach-Object { Write-Host "  $($_.Key): $($_.Value) ($($tables[$_.Key].SiteName))" }
    if ($existing.Count -eq $tables.Count) {
        Write-Host ''
        Write-Host ('Sites=' + (($tables.Keys | ForEach-Object { "${_}:$($existing[$_])" }) -join ','))
    }
    return
}

# -Recreate: 同じサイト名のサイトを消す (サイト名が重なっていれば残りも見つかるまで繰り返す)
for ($round = 0; $existing.Count -gt 0; $round++) {
    if ($round -ge 10) { throw '同じサイト名のサイトを消しきれませんでした。' }
    foreach ($entry in $existing.GetEnumerator()) {
        Write-Host "既存のサイトを消します: $($entry.Value) ($($tables[$entry.Key].SiteName))"
        Remove-Site $entry.Value
    }
    $existing = [ordered]@{}
    foreach ($name in $tables.Keys) {
        $found = Find-Site $customersId $tables[$name].SiteName
        if ($found -gt 0) { $existing[$name] = $found }
    }
}

Set-SiteName $customersId $tables.Customers.SiteName
$projectsId = New-Site $tables.Projects.Title 'Results' (Get-ProjectsSettings $customersId)
Set-SiteName $projectsId $tables.Projects.SiteName
$tasksId = New-Site $tables.Tasks.Title 'Issues' (Get-TasksSettings $projectsId)
Set-SiteName $tasksId $tables.Tasks.SiteName

$customerIds = foreach ($c in $customers) {
    New-Record $customersId @{
        Title           = $c.Title
        ClassHash       = @{ ClassA = $c.Industry; ClassB = $c.Phone }
        DescriptionHash = @{ DescriptionA = $c.Note }
    }
}
Write-Host "  取引先: $($customerIds.Count) 件"

$projectIds = foreach ($p in $projects) {
    $record = @{
        Title           = $p.Title
        Status          = $p.Status
        ClassHash       = @{ ClassA = [string]$customerIds[$p.Customer]; ClassB = $p.Kind }
        NumHash         = @{ NumA = $p.Amount }
        CheckHash       = @{ CheckA = $p.Checked }
        DescriptionHash = @{ DescriptionA = $p.Memo }
    }
    if ($p.Ordered) { $record.DateHash = @{ DateA = $p.Ordered } }
    New-Record $projectsId $record
}
Write-Host "  案件: $($projectIds.Count) 件"

$taskIds = foreach ($t in $tasks) {
    New-Record $tasksId @{
        Title          = $t.Title
        Status         = $t.Status
        StartTime      = $t.Start
        CompletionTime = $t.End
        WorkValue      = $t.Work
        ProgressRate   = $t.Progress
        ClassHash      = @{ ClassA = [string]$projectIds[$t.Project]; ClassB = $t.Person }
    }
}
Write-Host "  作業: $($taskIds.Count) 件"

Write-Host ''
Write-Host "作りました: 取引先 $customersId / 案件 $projectsId / 作業 $tasksId"
Write-Host '接続文字列の Sites:'
Write-Host "Sites=Customers:$customersId,Projects:$projectsId,Tasks:$tasksId"
