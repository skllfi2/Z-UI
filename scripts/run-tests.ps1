# run-tests.ps1 - Запуск тестов Z-UI 2.0
param(
    [string]$Configuration = "Debug",
    [switch]$UnitTests = $true,
    [switch]$IntegrationTests = $false
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path $PSScriptRoot -Parent

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Z-UI 2.0 Test Runner" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# Проверка наличия тестового проекта
$TestProject = Join-Path $ProjectDir "ZUI.Tests\ZUI.Tests.csproj"
if (-not (Test-Path $TestProject)) {
    Write-Warning "Test project not found: $TestProject"
    Write-Host "Creating minimal test project..." -ForegroundColor Yellow
    
    $TestDir = Join-Path $ProjectDir "ZUI.Tests"
    New-Item -ItemType Directory -Force -Path $TestDir | Out-Null
    
    @'<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.0">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Z-UI\Z-UI.csproj" />
  </ItemGroup>
</Project>
'@ | Out-File -FilePath $TestProject -Encoding UTF8

    # Создаём базовый тест
    $TestClassPath = Join-Path $TestDir "Services\AdaptiveEngineTests.cs"
    New-Item -ItemType Directory -Force -Path (Split-Path $TestClassPath) | Out-Null
    
    @'using Xunit;
using Z_UI.Services.AdaptiveEngine;

namespace ZUI.Tests.Services;

public class AdaptiveEngineTests
{
    [Fact]
    public void TrafficClassifier_ShouldDetectGaming()
    {
        var classifier = new TrafficClassifier();
        var result = classifier.Classify("steamcommunity.com", 443, System.Net.Sockets.ProtocolType.Tcp);
        
        Assert.Equal(TrafficCategory.Gaming, result.Category);
    }

    [Fact]
    public void TrafficClassifier_ShouldDetectAIService()
    {
        var classifier = new TrafficClassifier();
        var result = classifier.Classify("chatgpt.com", 443, System.Net.Sockets.ProtocolType.Tcp);
        
        Assert.Equal(TrafficCategory.AIService, result.Category);
    }

    [Fact]
    public void SmartStrategySelector_ShouldSelectDNSForAI()
    {
        // Arrange
        var selector = new SmartStrategySelector(null);
        var context = new TrafficContext 
        { 
            Domain = "chatgpt.com", 
            Category = TrafficCategory.AIService 
        };

        // Act
        var strategy = selector.SelectStrategy(context);

        // Assert
        Assert.Equal(StrategyType.DnsBypass, strategy.Type);
    }
}
'@ | Out-File -FilePath $TestClassPath -Encoding UTF8
}

# Запуск тестов
if ($UnitTests) {
    Write-Host "`nRunning unit tests..." -ForegroundColor Cyan
    dotnet test $TestProject --configuration $Configuration --no-build --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed"
        exit 1
    }
}

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "All tests passed!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Cyan
