import os

path = r'C:\Users\Lecoo\projects\RussianLocalization_NoWorkshop\RussianLocalization.csproj'
content = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AssemblyName>RussianLocalization</AssemblyName>
    <OutputType>Library</OutputType>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <OutputPath>bin</OutputPath>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\Assembly-CSharp.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\UnityEngine.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.UI">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\UnityEngine.UI.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="Unity.TextMeshPro">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\Unity.TextMeshPro.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\UnityEngine.CoreModule.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\0Harmony.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="Newtonsoft.Json">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\Newtonsoft.Json.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.UIElementsModule">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\UnityEngine.UIElementsModule.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.UIModule">
      <HintPath>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed\\UnityEngine.UIModule.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
</Project>
"""

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
print(f"Fixed light csproj at {path}")
