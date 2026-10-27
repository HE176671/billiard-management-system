param([string]$BaseUrl = 'http://localhost:5080', [switch]$CheckPresentationData)
$ErrorActionPreference = 'Stop'
$checks = 0
function Assert-Check($condition, [string]$label) {
    if (-not $condition) { throw "FAIL: $label" }
    $script:checks++
    Write-Output "PASS: $label"
}
function New-Client {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.CookieContainer = [Net.CookieContainer]::new()
    [Net.Http.HttpClient]::new($handler)
}
function Get-Page($client, [string]$path) {
    $client.GetAsync($BaseUrl + $path).GetAwaiter().GetResult()
}
function Get-Token($response) {
    $html = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $match = [regex]::Match($html, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $match.Success) { throw 'Anti-forgery token missing' }
    [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
}
function Post-Form($client, [string]$path, [hashtable]$data) {
    $pairs = [Collections.Generic.Dictionary[string,string]]::new()
    foreach ($item in $data.GetEnumerator()) { $pairs.Add($item.Key, [string]$item.Value) }
    $body = [Net.Http.FormUrlEncodedContent]::new($pairs)
    $client.PostAsync($BaseUrl + $path, $body).GetAwaiter().GetResult()
}
# Read the existing local password without printing or embedding it in this script.
$secretPath = Join-Path $env:APPDATA 'Microsoft/UserSecrets/d8114019-df86-4943-8de1-92b0ef494aa5/secrets.json'
$secret = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
$password = $secret.'DevSeed:AdminPassword'
if (-not $password) { throw 'DevSeed:AdminPassword is missing from user-secrets' }
$admin = New-Client
$customer = New-Client
$fixtureName = 'http.smoke.' + [guid]::NewGuid().ToString('N')
try {
    foreach ($path in @('/Dashboard','/Employee','/Customer','/MemberTier','/Pricing','/Revenue')) {
        $response = Get-Page $admin $path
        Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString().Contains('/Account/Login')) "Anonymous blocked: $path"
    }
    $login = Get-Page $admin '/Account/Login'
    $response = Post-Form $admin '/Account/Login' @{ Username='admin.demo'; Password=$password; __RequestVerificationToken=(Get-Token $login) }
    Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString() -eq '/Dashboard') 'Admin login redirects to Dashboard'
    foreach ($path in @('/Dashboard','/Employee','/Customer','/MemberTier','/MemberTier/Create','/Pricing','/Pricing/Create','/Revenue','/Profile/ChangePassword')) {
        $response = Get-Page $admin $path
        Assert-Check ([int]$response.StatusCode -eq 200) "Admin page renders: $path"
    }
    $response = Post-Form $admin '/Pricing/Create' @{ TableTypeId='1'; StartHour='08:00'; EndHour='09:00'; HourlyRate='50000' }
    Assert-Check ([int]$response.StatusCode -eq 400) 'POST without anti-forgery token rejected'
    $form = Get-Page $admin '/MemberTier/Create'
    $response = Post-Form $admin '/MemberTier/Create' @{ TierName='Invalid'; MinPoints='-1'; DiscountPercent='101'; __RequestVerificationToken=(Get-Token $form) }
    Assert-Check ([int]$response.StatusCode -eq 200 -and $response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('validation-summary-errors')) 'Invalid tier input returns validation errors'
    $response = Get-Page $admin '/Revenue/Export?from=2035-01-03&to=2035-01-01'
    Assert-Check ([int]$response.StatusCode -eq 400) 'Invalid report export rejected'
    $response = Get-Page $admin '/Revenue/Export?from=2035-01-01&to=2035-01-02'
    Assert-Check ([int]$response.StatusCode -eq 200 -and $response.Content.Headers.ContentType.MediaType -eq 'text/csv') 'HTTP CSV export'
    if ($CheckPresentationData) {
        foreach ($case in @(
            @{ From='2026-10-01'; To='2026-10-13'; Revenue=1110000; Payments=7 },
            @{ From='2026-10-12'; To='2026-10-12'; Revenue=390000; Payments=3 },
            @{ From='2026-10-13'; To='2026-10-13'; Revenue=350000; Payments=2 },
            @{ From='2026-09-28'; To='2026-10-13'; Revenue=1310000; Payments=8 }
        )) {
            $response = Get-Page $admin ("/Revenue/Export?from="+$case.From+"&to="+$case.To)
            $csv = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult().TrimStart([char]0xFEFF) | ConvertFrom-Csv
            $sum = ($csv | Measure-Object -Property 'Doanh thu VND' -Sum).Sum
            $count = ($csv | Measure-Object -Property 'So giao dich' -Sum).Sum
            Assert-Check ([int]$response.StatusCode -eq 200 -and $sum -eq $case.Revenue -and $count -eq $case.Payments) ("Demo report verified: "+$case.From+" to "+$case.To)
        }
    }

    $registration = Get-Page $customer '/Account/Register'
    $fixturePassword = [guid]::NewGuid().ToString('N') + '!Aa1'
    $response = Post-Form $customer '/Account/Register' @{ UserName=$fixtureName; Email=($fixtureName + '@example.invalid'); FullName='HTTP Smoke Customer'; Password=$fixturePassword; ConfirmPassword=$fixturePassword; __RequestVerificationToken=(Get-Token $registration) }
    Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString() -eq '/Customer/Portal') 'Customer registration and correct landing page'
    $response = Get-Page $customer '/Customer/Portal'
    Assert-Check ([int]$response.StatusCode -eq 200) 'Customer portal renders'
    $change = Get-Page $customer '/Profile/ChangePassword'
    $newPassword = [guid]::NewGuid().ToString('N') + '!Aa2'
    $response = Post-Form $customer '/Profile/ChangePassword' @{ CurrentPassword='WrongCurrent!123'; NewPassword=$newPassword; ConfirmPassword=$newPassword; __RequestVerificationToken=(Get-Token $change) }
    Assert-Check ([int]$response.StatusCode -eq 200 -and $response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('alert-danger')) 'Wrong current password rejected'
    $response = Post-Form $customer '/Profile/ChangePassword' @{ CurrentPassword=$fixturePassword; NewPassword=$newPassword; ConfirmPassword=$newPassword; __RequestVerificationToken=(Get-Token (Get-Page $customer '/Profile/ChangePassword')) }
    Assert-Check ([int]$response.StatusCode -eq 302) 'Customer changes password successfully'
    $fixturePassword = $newPassword
    Assert-Check ([int](Get-Page $customer '/Customer/Portal').StatusCode -eq 200) 'Current session refreshed after password change'
    foreach ($path in @('/Dashboard','/Employee','/Customer','/MemberTier','/Pricing','/Revenue')) {
        $response = Get-Page $customer $path
        Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString().Contains('/Account/AccessDenied')) "Customer denied admin page: $path"
    }
    $page = Get-Page $admin ("/Customer?search=" + $fixtureName)
    $html = $page.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $match = [regex]::Match($html, 'action="(/Customer/ToggleLock/[^"]+)"')
    Assert-Check ($match.Success) 'Registered customer appears in admin management'
    $response = Post-Form $admin $match.Groups[1].Value @{ __RequestVerificationToken=(Get-Token $page) }
    Assert-Check ([int]$response.StatusCode -eq 302) 'Admin locks customer through HTTP'
    $response = Get-Page $customer '/Customer/Portal'
    Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString().Contains('/Account/Login')) 'Locked customer cookie rejected on next request'
    $response = Post-Form $admin $match.Groups[1].Value @{ __RequestVerificationToken=(Get-Token (Get-Page $admin '/Customer')) }
    $login = Get-Page $customer '/Account/Login'
    $response = Post-Form $customer '/Account/Login' @{ Username=$fixtureName; Password=$fixturePassword; __RequestVerificationToken=(Get-Token $login) }
    Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString() -eq '/Customer/Portal') 'Unlocked customer can login again'
    $response = Post-Form $admin '/Account/Logout' @{ __RequestVerificationToken=(Get-Token (Get-Page $admin '/Dashboard')) }
    Assert-Check ([int]$response.StatusCode -eq 302) 'Logout succeeds'
    $response = Get-Page $admin '/Dashboard'
    Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString().Contains('/Account/Login')) 'Logout removes authenticated access'
    Write-Output "$checks HTTP checks passed."
}
finally {
    # Only remove this run's unique test account, with no business history.
    & sqlcmd -S localhost -d BilliardDB -E -C -b -Q "SET QUOTED_IDENTIFIER ON; BEGIN TRAN; DELETE ur FROM dbo.AspNetUserRoles ur JOIN dbo.AspNetUsers u ON ur.UserId=u.Id WHERE u.UserName=N'$fixtureName' AND u.Email=N'$fixtureName@example.invalid'; DELETE FROM dbo.AspNetUsers WHERE UserName=N'$fixtureName' AND Email=N'$fixtureName@example.invalid'; COMMIT;" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Test account cleanup failed' }
    $admin.Dispose(); $customer.Dispose()
    $password = $null; $secret = $null; $fixturePassword = $null
}
