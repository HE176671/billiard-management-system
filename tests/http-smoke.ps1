param([string]$BaseUrl = 'http://localhost:5000', [string]$SqlServer = 'localhost\SQLEXPRESS', [switch]$CheckPresentationData)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
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
$staffPassword = if ($secret.'DevSeed:StaffPassword') { $secret.'DevSeed:StaffPassword' } else { 'Staff@123' }
$admin = New-Client
$staff = New-Client
$customer = New-Client
$fixtureName = 'http.smoke.' + [guid]::NewGuid().ToString('N')
try {
    foreach ($path in @('/Dashboard','/Employee','/Customer','/MemberTier','/Pricing','/Revenue','/CustomerManagement','/CustomerManagement/Lookup','/CashierSimulator','/PointAdjustment','/CrmDashboard','/RewardItem','/TierConfig')) {
        $response = Get-Page $admin $path
        Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString().Contains('/Account/Login')) "Anonymous blocked: $path"
    }
    $login = Get-Page $admin '/Account/Login'
    $response = Post-Form $admin '/Account/Login' @{ Username='admin.demo'; Password=$password; __RequestVerificationToken=(Get-Token $login) }
    Assert-Check ([int]$response.StatusCode -eq 302 -and ($response.Headers.Location.ToString() -in @('/RoleManagement', '/Dashboard'))) 'Admin login redirects to landing page'
    foreach ($path in @('/Dashboard','/Employee','/Customer','/MemberTier','/MemberTier/Create','/Pricing','/Pricing/Create','/Revenue','/Profile/ChangePassword','/CustomerManagement','/CustomerManagement/Lookup','/CashierSimulator','/PointAdjustment','/CrmDashboard','/RewardItem','/TierConfig')) {
        $response = Get-Page $admin $path
        $isOk = ([int]$response.StatusCode -eq 200) -or ([int]$response.StatusCode -eq 302 -and $path -eq '/CustomerManagement/Lookup'); Assert-Check $isOk "Admin page renders: $path"
    }

    # Staff login and role boundary checks (TV2)
    $staffLogin = Get-Page $staff '/Account/Login'
    $staffResp = Post-Form $staff '/Account/Login' @{ Username='staff.demo01'; Password=$staffPassword; __RequestVerificationToken=(Get-Token $staffLogin) }
    Assert-Check ([int]$staffResp.StatusCode -eq 302) 'Staff login succeeds'
    foreach ($path in @('/CustomerManagement/Lookup','/CashierSimulator','/PointAdjustment')) {
        $response = Get-Page $staff $path
        $isOk = ([int]$response.StatusCode -eq 200) -or ([int]$response.StatusCode -eq 302 -and $path -eq '/CustomerManagement/Lookup'); Assert-Check $isOk "Staff page renders: $path"
    }
    foreach ($path in @('/CrmDashboard','/RewardItem','/TierConfig','/PointAdjustment/Pending')) {
        $response = Get-Page $staff $path
        Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString().Contains('/Account/AccessDenied')) "Staff denied admin-only page: $path"
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

    # Internal management software checks: Customer self-registration & CustomerPortal removed
    Assert-Check ([int](Get-Page $customer '/Account/Register').StatusCode -eq 404) 'Customer self-registration route disabled (404)'
    Assert-Check ([int](Get-Page $customer '/CustomerPortal/Profile').StatusCode -eq 404) 'Customer portal route disabled (404)'

    # Customer login is blocked on internal system
    $custLoginPage = Get-Page $customer '/Account/Login'
    $custLoginResp = Post-Form $customer '/Account/Login' @{ Username='customer.demo01'; Password='Customer@123'; __RequestVerificationToken=(Get-Token $custLoginPage) }
    Assert-Check ([int]$custLoginResp.StatusCode -eq 200 -and $custLoginResp.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('alert-danger')) 'Customer role blocked from internal login'

    # Staff wrong current password rejected on /Profile/ChangePassword
    $change = Get-Page $staff '/Profile/ChangePassword'
    $newPassword = [guid]::NewGuid().ToString('N') + '!Aa2'
    $response = Post-Form $staff '/Profile/ChangePassword' @{ CurrentPassword='WrongCurrent!123'; NewPassword=$newPassword; ConfirmPassword=$newPassword; __RequestVerificationToken=(Get-Token $change) }
    Assert-Check ([int]$response.StatusCode -eq 200 -and $response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains('alert-danger')) 'Wrong current password rejected'

    $response = Post-Form $admin '/Account/Logout' @{ __RequestVerificationToken=(Get-Token (Get-Page $admin '/Dashboard')) }
    Assert-Check ([int]$response.StatusCode -eq 302) 'Logout succeeds'
    $response = Get-Page $admin '/Dashboard'
    Assert-Check ([int]$response.StatusCode -eq 302 -and $response.Headers.Location.ToString().Contains('/Account/Login')) 'Logout removes authenticated access'
    Write-Output "$checks HTTP checks passed."
}
finally {
    $admin.Dispose(); $staff.Dispose(); $customer.Dispose()
    $password = $null; $staffPassword = $null; $secret = $null
}
