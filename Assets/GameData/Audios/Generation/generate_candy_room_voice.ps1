param(
    [string]$OutputDirectory = ""
)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) "SFX\Character\CandyRoomVoice"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Add-Type -AssemblyName System.Speech

$phrases = @(
    @{ Name = "Voice_Ouch_01.wav"; Text = "Ouch!"; Rate = 2 },
    @{ Name = "Voice_Ouch_02.wav"; Text = "Ow!"; Rate = 3 },
    @{ Name = "Voice_Ooo_01.wav"; Text = "Ooooh!"; Rate = 1 },
    @{ Name = "Voice_Ooo_02.wav"; Text = "Whoa!"; Rate = 2 },
    @{ Name = "Voice_DontHitMe_01.wav"; Text = "Don't hit me!"; Rate = 2 },
    @{ Name = "Voice_DontHitMe_02.wav"; Text = "Hey, don't hit me!"; Rate = 2 },
    @{ Name = "Voice_Man_01.wav"; Text = "Maaaan!"; Rate = 1 },
    @{ Name = "Voice_Man_02.wav"; Text = "Oh, man!"; Rate = 2 }
)

foreach ($phrase in $phrases) {
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.Rate = $phrase.Rate
        $synth.Volume = 100
        $target = Join-Path $OutputDirectory $phrase.Name
        $synth.SetOutputToWaveFile($target)
        $synth.Speak($phrase.Text)
        $synth.SetOutputToNull()
    }
    finally {
        $synth.Dispose()
    }
}

Write-Output "Candy-room voice masters generated at $OutputDirectory"
