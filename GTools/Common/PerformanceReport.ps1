function Write-PerformanceReport {
    param([Parameter(Mandatory)][string]$Directory)
    $html = [Text.StringBuilder]::new()
    $null = $html.Append(@'
<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>爆破性能跑测</title>
<style>body{font:15px/1.65 system-ui,"Microsoft YaHei",sans-serif;max-width:1450px;margin:40px auto;padding:0 24px;color:#23313d;background:#f5f7fa}h1{font-size:30px}h2{margin-top:38px}table{border-collapse:collapse;width:100%;background:white;font-variant-numeric:tabular-nums}th,td{text-align:right;padding:10px 12px;border-bottom:1px solid #dde3e9}th{background:#172c3e;color:white}th:first-child,td:first-child{text-align:left}a{color:#12648c}.note{background:#e6eff5;padding:16px 20px;border-left:4px solid #2278a3}.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}.grid img{width:100%}code{background:#e3e9ef;padding:2px 5px}.scroll{overflow-x:auto}</style>
<h1>爆破模式实战性能报告</h1>
<p class="note">真实地图、一个脚本玩家和九个 AI，客户端与权威服在同一台机器运行。各阶段预热后采样，截图和导出排除。AI 对局会随时序变化，对照组用于定位；请结合存活比例、位置和开火事件解读。低于 0 的指标表示不可用。P99 表示 99% 的采样帧不超过该耗时；极少数严重卡顿还需查看最大值和长帧数。</p>
'@)
    foreach ($folder in Get-ChildItem -LiteralPath $Directory -Directory | Sort-Object Name) {
        $path = Join-Path $folder.FullName 'client-summary.json'
        if (!(Test-Path -LiteralPath $path)) { continue }
        $summary = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $coveragePath = Join-Path $folder.FullName 'coverage.json'
        if (!(Test-Path -LiteralPath $coveragePath)) { continue }
        $coverage = Get-Content -LiteralPath $coveragePath -Raw | ConvertFrom-Json
        $name = [Net.WebUtility]::HtmlEncode($folder.Name)
        $gpu = [Net.WebUtility]::HtmlEncode($summary.gpu)
        if (!(Test-Path -LiteralPath (Join-Path $folder.FullName 'client-result.txt'))) {
            $null = $html.Append("<p class='note'>$name 未通过场景覆盖或运行检查，以下数据只用于诊断，不纳入成功基线。</p>")
        }
        $null = $html.Append("<h2>$name</h2><p>$gpu · $($summary.width) × $($summary.height) · $($summary.quality) · $($coverage.view) · $($summary.graphicsApi)<br>实测移动 $([Math]::Round($coverage.traveledMetres,1)) m · 视角旋转 $([Math]::Round($coverage.orbitDegrees,1))° · 收到开火事件 $($coverage.shots) 次</p>")
        $null = $html.Append('<div class="scroll"><table><tr><th>阶段</th><th>FPS</th><th>P99 帧/ms</th><th>最大帧/ms</th><th>&gt;33.3 ms 帧数</th><th>GPU 均值/ms</th><th>视野均值/ms</th><th>遮挡均值/ms</th><th>GC KB/帧</th><th>存活比例</th></tr>')
        foreach ($phase in $summary.summaries) {
            $metrics = @{}; foreach ($m in $phase.metrics) { $metrics[$m.metric]=$m }
            $label = switch($phase.phase){'idle'{'静止观察'} 'traverse_orbit'{'移动与转视角'} 'combat'{'交战'} default{$phase.phase}}
            $cells = @($phase.fps,$metrics.frame_ms.p99,$metrics.frame_ms.max,$phase.over33ms,$metrics.gpu_frame_ms.mean,
                $metrics.vision_ms.mean,$metrics.cutaway_ms.mean,($metrics.gc_alloc_bytes.mean/1024),$phase.aliveFraction)
            $null = $html.Append("<tr><td>$label</td>")
            foreach ($value in $cells) { $display = if($value -lt 0){'不可用'}else{([double]$value).ToString('F2',[Globalization.CultureInfo]::InvariantCulture)}; $null = $html.Append("<td>$display</td>") }
            $null = $html.Append('</tr>')
        }
        $null = $html.Append("</table></div><p><a href='$name/client-frames.csv'>逐帧 CSV</a> · <a href='$name/client-summary.json'>完整客户端指标</a> · <a href='$name/server-summary.json'>服务端指标</a> · <a href='$name/coverage.json'>场景覆盖</a></p><div class='grid'>")
        foreach ($phase in @('idle','traverse_orbit','combat')) { $null = $html.Append("<a href='$name/$phase.png'><img src='$name/$phase.png' alt='$phase'></a>") }
        $null = $html.Append('</div>')
    }
    $null = $html.Append('<p>计时区间为包含子调用的 CPU 时间，不应相加当作总帧耗时。GPU timing 有数帧延迟；原始 CSV 中同一行的 GPU 和脚本数据并非严格对齐。</p></html>')
    [IO.File]::WriteAllText((Join-Path $Directory 'report.html'),$html.ToString(),[Text.UTF8Encoding]::new($false))
}
