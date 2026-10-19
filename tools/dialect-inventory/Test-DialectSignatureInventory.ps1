[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$inventoryModulePath = Join-Path $ProjectRoot 'tools\dialect-inventory\DialectInventory.psm1'
$signatureModulePath = Join-Path $ProjectRoot 'tools\dialect-inventory\DialectSignatureInventory.psm1'
$catalogPath = Join-Path $ProjectRoot 'tools\dialect-inventory\dialect-classification.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-signature-test-' + [Guid]::NewGuid().ToString('N'))
$utf8NoBom = New-Object Text.UTF8Encoding($false)

function Assert-SignatureContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Write-TestText {
    param([string]$Path, [string]$Value)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Value, $utf8NoBom)
}

function Find-TestLine {
    param([string]$Text, [string]$Needle)
    $lines = $Text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Contains($Needle)) { return $i + 1 }
    }
    throw "Synthetic source line not found: $Needle"
}

try {
    if (-not (Test-Path -LiteralPath $signatureModulePath -PathType Leaf)) {
        throw "Dialect signature inventory module is missing: $signatureModulePath"
    }
    Import-Module $inventoryModulePath -Force
    Import-Module $signatureModulePath -Force

    $inventory = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $catalogPath
    $actualOutput = Join-Path $testRoot 'actual-signatures.json'
    $actual = New-DialectSignatureInventory -ProjectRoot $ProjectRoot -Inventory $inventory -OutputPath $actualOutput
    Assert-SignatureContract ($actual.workPackage -eq 'M0-DIA-03') 'Unexpected signature inventory work package.'
    Assert-SignatureContract ($actual.executionStatus -eq 'InProgress' -and $actual.gateStatus -eq 'Blocked' -and $actual.blockerCode -eq 'EvidenceMissing') 'M0 gate status was incorrectly advanced.'
    Assert-SignatureContract ($actual.result -eq 'Partial') 'Static signature inventory must not claim behavior completion.'
    Assert-SignatureContract ($actual.sourceInventoryHash -eq $inventory.canonicalHash) 'Signature inventory source hash mismatch.'
    Assert-SignatureContract ($actual.instructionCount -eq 327) "Unexpected instruction descriptor count: $($actual.instructionCount)."
    Assert-SignatureContract ($actual.expressionFunctionCount -eq 362) "Unexpected expression descriptor count: $($actual.expressionFunctionCount)."
    Assert-SignatureContract ($actual.coverage.instructionRegistrationSourceResolvedCount -eq 327) 'Not all instruction registration sources were resolved.'
    Assert-SignatureContract ($actual.coverage.instructionBindingUnknownCount -eq 0) 'Instruction binding kind extraction regressed.'
    Assert-SignatureContract ($actual.coverage.expressionHandlerSourceResolvedCount -ge 350) 'Expression handler source coverage unexpectedly shrank.'
    Assert-SignatureContract ($actual.coverage.expressionReturnResolvedCount -gt 250) 'Expression return type coverage unexpectedly shrank.'
    Assert-SignatureContract ($actual.coverage.expressionArgumentResolvedCount -gt 250) 'Expression argument schema coverage unexpectedly shrank.'
    Assert-SignatureContract ($actual.coverage.instructionInputWaitCandidateCount -gt 0) 'No instruction input-wait candidates were detected.'
    Assert-SignatureContract ($actual.descriptorSetHash -match '^[0-9a-f]{64}$') 'Signature descriptor set hash is invalid.'
    Assert-SignatureContract ((Test-Path -LiteralPath $actualOutput -PathType Leaf)) 'Signature inventory report was not written.'

    $repeat = New-DialectSignatureInventory -ProjectRoot $ProjectRoot -Inventory $inventory
    Assert-SignatureContract ($repeat.descriptorSetHash -eq $actual.descriptorSetHash) 'Signature descriptor hash changed between identical scans.'

    $syntheticRoot = Join-Path $testRoot 'synthetic'
    $identifierText = @'
namespace MinorShift.Emuera.GameProc.Function {
    internal sealed class FunctionIdentifier {
        static void Register() {
            addFunction(FunctionCode.DIRECT, argb[FunctionArgType.INT_EXPRESSION], METHOD_SAFE);
            addFunction(FunctionCode.HANDLER, new HandlerInstruction(), IS_PRINT);
        }
    }
}
'@
    $instructionText = @'
namespace MinorShift.Emuera.GameProc.Function {
    internal sealed class HandlerInstruction : AbstractInstruction {
        public HandlerInstruction() {
            string ignored = "}"; // { ignored brace
            ArgBuilder = ArgumentParser.GetArgumentBuilder(FunctionArgType.SP_INPUT);
            flag = IS_INPUT | IS_PRINT;
        }
        public override void DoInstruction(ExpressionMediator exm, InstructionLine line, ProcessState state) {
            exm.Console.WaitInput(new InputRequest());
        }
    }
}
'@
    $methodText = @'
namespace MinorShift.Emuera.GameData.Function {
    internal sealed class CompleteMethod : FunctionMethod {
        public CompleteMethod() {
            ReturnType = EraType.Integer;
            argumentTypeArray = new EraType[] { EraType.Integer, EraType.String };
        }
    }
    internal sealed class ConditionalMethod : FunctionMethod {
        public ConditionalMethod(bool text) {
            if (text) ReturnType = EraType.String;
            else ReturnType = EraType.Integer;
            argumentTypeArray = null;
        }
        public override string CheckArgumentType(string name, IOperandTerm[] args) { return null; }
    }
}
'@
    Write-TestText -Path (Join-Path $syntheticRoot 'Scripts\Emuera\GameProc\Function\FunctionIdentifier.cs') -Value $identifierText
    Write-TestText -Path (Join-Path $syntheticRoot 'Scripts\Emuera\GameProc\Function\Instraction.Child.cs') -Value $instructionText
    Write-TestText -Path (Join-Path $syntheticRoot 'Scripts\Emuera\GameData\Function\Creator.Method.cs') -Value $methodText

    $syntheticInventory = [pscustomobject]@{
        canonicalHash = ('c' * 64)
        instructionRegistrations = @(
            [pscustomobject]@{ publicKey='DIRECT'; sourceFile='scripts/Emuera/GameProc/Function/FunctionIdentifier.cs'; sourceLine=(Find-TestLine $identifierText 'FunctionCode.DIRECT'); handler='argb[FunctionArgType.INT_EXPRESSION], METHOD_SAFE'; targetModule='gemuera.v24'; currentGuard='none'; currentContribution='test'; provenanceWarning='' },
            [pscustomobject]@{ publicKey='HANDLER'; sourceFile='scripts/Emuera/GameProc/Function/FunctionIdentifier.cs'; sourceLine=(Find-TestLine $identifierText 'FunctionCode.HANDLER'); handler='HandlerInstruction'; targetModule='game.snake'; currentGuard='none'; currentContribution='test'; provenanceWarning='' }
        )
        expressionRegistrations = @(
            [pscustomobject]@{ publicKey='COMPLETE'; handler='CompleteMethod'; targetModule='gemuera.v24'; currentGuard='none'; currentContribution='test'; provenanceWarning='' },
            [pscustomobject]@{ publicKey='CONDITIONAL'; handler='ConditionalMethod'; targetModule='game.snake'; currentGuard='none'; currentContribution='test'; provenanceWarning='' },
            [pscustomobject]@{ publicKey='MISSING'; handler='MissingMethod'; targetModule='game.snake'; currentGuard='none'; currentContribution='test'; provenanceWarning='' }
        )
    }
    $synthetic = New-DialectSignatureInventory -ProjectRoot $syntheticRoot -Inventory $syntheticInventory
    $direct = @($synthetic.instructions | Where-Object publicKey -eq 'DIRECT')[0]
    $handler = @($synthetic.instructions | Where-Object publicKey -eq 'HANDLER')[0]
    $complete = @($synthetic.expressionFunctions | Where-Object publicKey -eq 'COMPLETE')[0]
    $conditional = @($synthetic.expressionFunctions | Where-Object publicKey -eq 'CONDITIONAL')[0]
    $missing = @($synthetic.expressionFunctions | Where-Object publicKey -eq 'MISSING')[0]
    Assert-SignatureContract ($direct.bindingKind -eq 'ArgumentBuilderTable' -and $direct.argumentSchemaStatus -eq 'Resolved' -and $direct.argumentSchemaCandidates[0] -eq 'FunctionArgType.INT_EXPRESSION') 'Direct ArgumentBuilder signature was not resolved.'
    Assert-SignatureContract ($handler.bindingKind -eq 'InstructionHandler' -and $handler.argumentSchemaStatus -eq 'Resolved') 'Instruction-owned argument schema was not resolved.'
    Assert-SignatureContract ($handler.completionModeCandidate -eq 'InputWaitCandidate') 'WaitInput body was not classified as an input-wait candidate.'
    Assert-SignatureContract ($complete.returnTypeStatus -eq 'Resolved' -and $complete.returnTypeCandidates[0] -eq 'EraType.Integer') 'Function return type was not resolved.'
    Assert-SignatureContract ($complete.argumentSchemaStatus -eq 'Resolved' -and $complete.signatureStatus -eq 'CompleteStatic') 'Complete function signature was not classified correctly.'
    Assert-SignatureContract ($conditional.returnTypeStatus -eq 'Conditional' -and $conditional.overridesArgumentCheck) 'Conditional function signature was incorrectly flattened.'
    Assert-SignatureContract ($missing.handlerSourceStatus -eq 'Unresolved' -and $missing.signatureStatus -eq 'Unresolved') 'Missing handler source was not kept Unresolved.'

    Write-TestText -Path (Join-Path $syntheticRoot 'Scripts\Emuera\GameData\Function\Creator.Method.cs') -Value $methodText.Replace('EraType.Integer;', 'EraType.Float;')
    $changed = New-DialectSignatureInventory -ProjectRoot $syntheticRoot -Inventory $syntheticInventory
    Assert-SignatureContract ($changed.descriptorSetHash -ne $synthetic.descriptorSetHash) 'Descriptor hash did not change after signature source changed.'

    Write-Output 'M0 dialect signature inventory contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
    if ($resolvedTest.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        $resolvedTest.Contains('gemuera-m0-dia-signature-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
