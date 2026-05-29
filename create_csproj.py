import os

file_path = '../RussianLocalization_NoWorkshop/RussianLocalization.csproj'

csproj_content = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>RussianLocalization</AssemblyName>
    <Version>1.0.1</Version>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <LangVersion>latest</LangVersion>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <OutputPath>bin</OutputPath>
    <ManagedDir>D:\\steam\\steamapps\\common\\Caves of Qud\\CoQ_Data\\Managed</ManagedDir>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="UnityEngine">
      <HintPath>$(ManagedDir)\\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(ManagedDir)\\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.UI">
      <HintPath>$(ManagedDir)\\UnityEngine.UI.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.UIElementsModule">
      <HintPath>$(ManagedDir)\\UnityEngine.UIElementsModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>$(ManagedDir)\\UnityEngine.IMGUIModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Unity.TextMeshPro">
      <HintPath>$(ManagedDir)\\Unity.TextMeshPro.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(ManagedDir)\\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ManagedDir)\\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Newtonsoft.Json">
      <HintPath>$(ManagedDir)\\Newtonsoft.Json.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="dictionary.json" />
    <EmbeddedResource Include="word_dictionary.json" />
    <EmbeddedResource Include="pattern_dictionary.json" />
  </ItemGroup>
</Project>
"""

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(csproj_content)

print("Project file created successfully.")
