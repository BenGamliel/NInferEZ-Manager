using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NInferManager.Contracts;

namespace NInferManager.WinUI;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _converterTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly Dictionary<string,CheckBox> _converterComponents=[];
    private readonly Dictionary<string,TextBox> _converterSources=[];
    private ConversionCapabilities? _converterCapabilities;
    private ConversionStatus? _conversionStatus;
    private ConversionRequest? _checkedConversion;
    private bool _loadingConverter;
    private bool _pollingConverter;
    private bool _settingConverterControls;
    private bool _converterReady;
    private bool _converterStarting;
    private string? _lastConversionNotice;
    private string? _suggestedConversionOutput;
    private string? _suggestedConfigFolder;

    private void InitializeConverter()
    {
        _converterReady=true;
        ConverterProfilePicker.SelectedIndex=0;
        _converterTimer.Tick+=async(_,_)=>await PollConverterAsync();
        ToolTipService.SetToolTip(ConverterProfilePicker,"Auto detects the conversion policy. Q4–Q8 trade file size for precision; BF16 and FP8 may need more VRAM. These do not change your source.");
        ToolTipService.SetToolTip(ConverterConfigBox,"GGUF needs the matching Hugging Face config.json and tokenizer files. The converter uses the source folder automatically when they are present.");
        ConverterProfilePicker.Header=HelpLabel("Output profile","Auto selects the converter's source-dependent policy. Preserve keeps supported encoded weights; it cannot preserve every GGUF geometry. Q4–Q8 explicitly re-quantize projections.");
        ConverterConfigBox.Header=HelpLabel("Matching config/tokenizer folder (GGUF)","Provide the source model's matching config.json and tokenizer. We detect them beside the file when available; weights alone are not enough.");
        ConverterRowsBox.Header=HelpLabel("Rows per conversion chunk","Smaller chunks reduce peak RAM usage but may take longer. They do not change model quality.");
        ConverterShardBox.Header=HelpLabel("Maximum shard size (GB)","Large models are split into output shards. Keep the entry and all generated shard files together.");
        ConverterRecipePicker.Header=HelpLabel("Built-in recipe","Applies only to the Recipe profile. These names come directly from the converter, not an invented model catalog.");
        ConverterCustomRecipeBox.Header=HelpLabel("Custom Python recipe","Overrides the selected built-in recipe in Recipe mode. This runs Python code with your account's file access; use only trusted files.");
        ConverterTemplateBox.Header=HelpLabel("Chat template override","Optional chat_template.jinja file controlling how messages are formatted. Leave blank to use matching source resources.");
        ConverterValidateBox.Header=HelpLabel("Test with matching ninfer-serve.exe","Optional real GPU text inference after conversion. Requires free VRAM and a compatible engine. Blank means structural checks only.");
    }

    private async Task OpenConverterAsync()
    {
        if(_loadingConverter || _closing)return;
        _loadingConverter=true;
        try
        {
            if(_converterCapabilities is null)
            {
                ConverterStatusText.Text="Starting the local conversion runtime…";
                _converterCapabilities=await _backend.ConversionCapabilitiesAsync()
                    ??throw new InvalidOperationException("The converter did not report its available options.");
                _settingConverterControls=true;
                try
                {
                    ConverterProfilePicker.Items.Clear();
                    foreach(var profile in _converterCapabilities.Profiles)
                        ConverterProfilePicker.Items.Add(new ComboBoxItem{Tag=profile,Content=profile switch
                        {"auto"=>"Auto · detect the appropriate conversion","preserve"=>"Preserve · keep supported source encoding","recipe"=>"Recipe · use a conversion recipe",_=>profile.ToUpperInvariant()}});
                    ConverterProfilePicker.SelectedIndex=0;
                    ConverterRecipePicker.ItemsSource=_converterCapabilities.Recipes;
                    InitializeConversionEditors();
                    foreach(var component in _converterCapabilities.Components.Where(x=>x!="text"))
                    {
                        var check=new CheckBox{Content=component switch{"vision"=>"Vision · image input","mtp"=>"MTP · multi-token prediction","dflash"=>"DFlash companion","dflash2"=>"DFlash2 companion",_=>component}};
                        check.Checked+=ConverterInput_Changed;check.Unchecked+=ConverterInput_Changed;
                        _converterComponents.Add(component,check);ConverterComponentPanel.Children.Add(check);
                        ToolTipService.SetToolTip(check,"Include this component only when its matching weights/resources are available. The compatibility check verifies the actual conversion plan.");
                    }
                    foreach(var source in new[]{"quantized","vision","mtp","dflash","dflash2"})
                    {
                        var box=new TextBox{Header=source switch{"quantized"=>"Separate quantized source (optional)",_=>$"{source.ToUpperInvariant()} companion path"},PlaceholderText="File or checkpoint folder path"};
                        box.TextChanged+=ConverterInput_Changed;
                        _converterSources.Add(source,box);ConverterSourcesPanel.Children.Add(ConversionPathEditor(box,"fileOrFolder"));
                    }
                }
                finally{_settingConverterControls=false;}
            }
            await PollConverterAsync();
            UpdateConverterControls();
        }
        catch(Exception ex){ShowInfoBar(ConverterInfo,"Converter unavailable",ex.Message,InfoBarSeverity.Error);ConverterStatusText.Text=ex.Message;}
        finally{_loadingConverter=false;}
    }

    private void ConverterInput_Changed(object sender,RoutedEventArgs args)
    {
        if(!_converterReady || _settingConverterControls)return;
        if(_converterResources.TryGetValue("chat_template.jinja",out var template))
        {
            _settingConverterControls=true;
            try
            {
                if(ReferenceEquals(sender,ConverterTemplateBox)&&template.Text!=ConverterTemplateBox.Text)template.Text=ConverterTemplateBox.Text;
                else if(ReferenceEquals(sender,template)&&ConverterTemplateBox.Text!=template.Text)ConverterTemplateBox.Text=template.Text;
            }
            finally{_settingConverterControls=false;}
        }
        _checkedConversion=null;
        if(ReferenceEquals(sender,ConverterSourceBox))SuggestConversionPaths();
        InvalidateConversionWorkflow(sender);
        UpdateConverterControls();
    }
    private void ConverterNumber_Changed(NumberBox sender,NumberBoxValueChangedEventArgs args)=>ConverterInput_Changed(sender,new RoutedEventArgs());

    private void SuggestConversionPaths()
    {
        var source=ConverterSourceBox.Text.Trim();
        var artifact=Path.GetExtension(source).Equals(".ninfer",StringComparison.OrdinalIgnoreCase);
        var gguf=Path.GetExtension(source).Equals(".gguf",StringComparison.OrdinalIgnoreCase);
        ConverterSourceHint.Text=artifact?"NInfer entry detected: v3 is repacked losslessly; recognized official v2 is upgraded. Existing components and encoding are kept.":gguf?"GGUF detected: provide matching config/tokenizer resources. Auto keeps qualified block encodings, otherwise the conversion plan may re-encode them.":"Checkpoint detected: architecture, weight encoding and resource compatibility are verified by the converter.";
        try
        {
            var full=Path.GetFullPath(source);
            var folder=Directory.Exists(full)?full:Path.GetDirectoryName(full)!;
            _settingConverterControls=true;
            try
            {
                if(string.IsNullOrWhiteSpace(ConverterConfigBox.Text) || ConverterConfigBox.Text==_suggestedConfigFolder)
                {
                    _suggestedConfigFolder=File.Exists(Path.Combine(folder,"config.json"))?folder:"";
                    ConverterConfigBox.Text=_suggestedConfigFolder;
                }
                if(string.IsNullOrWhiteSpace(ConverterOutputBox.Text)||ConverterOutputBox.Text==_suggestedConversionOutput)
                {
                    var appRoot=Path.GetDirectoryName(Environment.ProcessPath)!;
                    var models=File.Exists(Path.Combine(appRoot,"portable.mode"))?Path.Combine(appRoot,"Models"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NInferEZ Manager","Models");
                    var name=Directory.Exists(full)?Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)):Path.GetFileNameWithoutExtension(full);
                    _suggestedConversionOutput=Path.Combine(models,name+"-converted.ninfer");
                    ConverterOutputBox.Text=_suggestedConversionOutput;
                }
            }
            finally{_settingConverterControls=false;}
        }
        catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException){ }
    }

    private ConversionRequest ReadConversionRequest()
    {
        var artifact=Path.GetExtension(ConverterSourceBox.Text).Equals(".ninfer",StringComparison.OrdinalIgnoreCase);
        var proposal=!artifact&&ConverterProposalBox.IsChecked==true;
        if(!artifact&&(!double.IsFinite(ConverterRowsBox.Value)||ConverterRowsBox.Value!=Math.Round(ConverterRowsBox.Value))||
            proposal&&(!double.IsFinite(ConverterProposalRowsBox.Value)||ConverterProposalRowsBox.Value!=Math.Round(ConverterProposalRowsBox.Value))||
            !double.IsFinite(ConverterShardBox.Value))throw new InvalidOperationException("Enter whole-number chunk/proposal rows and a valid maximum shard size.");
        var profile=artifact?"preserve":(ConverterProfilePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString()??"auto";
        string? Value(TextBox box)=>string.IsNullOrWhiteSpace(box.Text)?null:box.Text.Trim();
        var sources=artifact?new Dictionary<string,string>():ReadConversionMap(ConverterExtraSourcesBox,"Additional named sources");
        if(!artifact)foreach(var item in _converterSources.Where(x=>!string.IsNullOrWhiteSpace(x.Value.Text)&& (x.Key=="quantized"||_converterComponents.GetValueOrDefault(x.Key)?.IsChecked==true)))sources[item.Key]=item.Value.Text.Trim();
        var template=artifact?null:Value(ConverterTemplateBox);
        var resources=artifact?new Dictionary<string,string>():_converterResources.Where(x=>!string.IsNullOrWhiteSpace(x.Value.Text)&&( !x.Key.Contains("preprocessor")||_converterComponents.GetValueOrDefault("vision")?.IsChecked==true)).ToDictionary(x=>x.Key,x=>x.Value.Text.Trim());
        if(template is not null)resources["chat_template.jinja"]=template;
        return new ConversionRequest
        {
            Source=ConverterSourceBox.Text.Trim(),Output=ConverterOutputBox.Text.Trim(),Profile=profile,
            ConfigFolder=artifact?null:Value(ConverterConfigBox),
            ConfigFile=artifact?null:Value(_converterConfigFile),ResourceFolder=artifact?null:Value(_converterResourceFolder),
            Components=artifact?["text"]:new[]{"text"}.Concat(_converterComponents.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key)).ToArray(),
            Sources=sources,Resources=resources,ShardOverrides=artifact?[]:ReadConversionMap(ConverterShardsBox,"Shard replacements"),
            TensorOverrides=artifact?[]:_conversionAssignments.Select(x=>x.Read()).ToArray(),
            Recipe=profile=="recipe"||_conversionWizard?(Value(ConverterCustomRecipeBox)??ConverterRecipePicker.SelectedItem?.ToString()):null,
            Override=artifact?null:Value(ConverterOverrideBox),AllowCustomCode=ConverterTrustCodeBox.IsChecked==true,
            RowsPerChunk=artifact?512:checked((int)ConverterRowsBox.Value),MaxFileBytes=checked((long)(ConverterShardBox.Value*1_000_000_000)),
            Name=artifact?null:Value(ConverterNameBox),Proposal=proposal,
            ProposalRows=proposal?checked((int)ConverterProposalRowsBox.Value):131072,Ranking=proposal?Value(ConverterRankingBox):null,
            ValidateEngine=Value(ConverterValidateBox),ValidateTimeoutSeconds=ReadConversionInteger(ConverterValidateTimeoutBox,"Engine test timeout"),
            Device=ConverterDevicePicker.SelectedItem?.ToString()??"cpu"
        };
    }

    private void UpdateConverterControls()
    {
        if(!_converterReady)return;
        var busy=_converterStarting||_conversionStatus?.Running==true;
        var artifact=Path.GetExtension(ConverterSourceBox.Text).Equals(".ninfer",StringComparison.OrdinalIgnoreCase);
        var gguf=Path.GetExtension(ConverterSourceBox.Text).Equals(".gguf",StringComparison.OrdinalIgnoreCase);
        var recipe=_conversionWizard||(ConverterProfilePicker.SelectedItem as ComboBoxItem)?.Tag?.ToString()=="recipe";
        ConverterSourceBox.IsEnabled=ConverterOutputBox.IsEnabled=ConverterFileButton.IsEnabled=ConverterFolderButton.IsEnabled=ConverterOutputButton.IsEnabled=!busy;
        ConverterConfigBox.IsEnabled=!busy&&!artifact;
        ConverterConfigButton.IsEnabled=!busy&&!artifact;
        ConverterConfigBox.Visibility=ConverterConfigButton.Visibility=!artifact?Visibility.Visible:Visibility.Collapsed;
        ConverterProfilePicker.IsEnabled=!busy&&!artifact;
        ConverterAdvanced.IsEnabled=!busy;
        foreach(var component in _converterComponents.Values)component.IsEnabled=!busy&&!artifact;
        ConverterRecipePicker.IsEnabled=ConverterCustomRecipeBox.IsEnabled=!busy&&!artifact&&recipe;
        ConverterOverrideBox.IsEnabled=ConverterTemplateBox.IsEnabled=ConverterNameBox.IsEnabled=ConverterProposalBox.IsEnabled=!busy&&!artifact;
        ConverterTrustCodeBox.IsEnabled=!busy&&!artifact;
        ConverterProposalRowsBox.IsEnabled=ConverterRankingBox.IsEnabled=!busy&&!artifact&&ConverterProposalBox.IsChecked==true;
        ConverterRowsBox.IsEnabled=!busy&&!artifact;
        foreach(var source in _converterSources)source.Value.IsEnabled=!busy&&!artifact&&(source.Key=="quantized"||_converterComponents.GetValueOrDefault(source.Key)?.IsChecked==true);
        SetConversionEditorsEnabled(ConverterResourcesCard,!busy&&!artifact);
        UpdateConversionWorkflow(busy,artifact);
        ConverterCheckButton.IsEnabled=!busy&&_converterCapabilities is not null&&!string.IsNullOrWhiteSpace(ConverterSourceBox.Text)&&!string.IsNullOrWhiteSpace(ConverterOutputBox.Text);
        ConverterRunButton.IsEnabled=!busy&&_conversionStatus?.CanConvert==true&&_checkedConversion is not null;
        ConverterCancelButton.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        ConverterCancelButton.IsEnabled=_conversionStatus?.Running==true&&_conversionStatus.Cancelling!=true;
        ConverterProgress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        ConverterProfileHint.Text=artifact?"Lossless repack or recognized v2 upgrade: quantization and optional-component changes do not apply.":"Auto preserves supported encoded weights where possible. Other sources may be re-quantized; review the conversion plan.";
    }

    private async void ConverterFile_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var picker=new Windows.Storage.Pickers.FileOpenPicker();
            foreach(var extension in new[]{".gguf",".safetensors",".json",".ninfer"})picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
            if(await picker.PickSingleFileAsync() is { } file)ConverterSourceBox.Text=file.Path;
        }
        catch(Exception ex){ConverterError(ex);}
    }
    private async void ConverterFolder_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var picker=new Windows.Storage.Pickers.FolderPicker();picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
            if(await picker.PickSingleFolderAsync() is { } folder)ConverterSourceBox.Text=folder.Path;
        }
        catch(Exception ex){ConverterError(ex);}
    }
    private async void ConverterConfig_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var picker=new Windows.Storage.Pickers.FolderPicker();picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
            if(await picker.PickSingleFolderAsync() is { } folder)ConverterConfigBox.Text=folder.Path;
        }
        catch(Exception ex){ConverterError(ex);}
    }
    private async void ConverterOutput_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var picker=new Windows.Storage.Pickers.FolderPicker();picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
            if(await picker.PickSingleFolderAsync() is { } folder)
            {
                var name=Path.GetFileName(ConverterOutputBox.Text);
                ConverterOutputBox.Text=Path.Combine(folder.Path,string.IsNullOrWhiteSpace(name)?"converted-model.ninfer":name);
            }
        }
        catch(Exception ex){ConverterError(ex);}
    }
    private async void ConverterCheck_Click(object sender,RoutedEventArgs e)=>await BeginConversionAsync("check");
    private async void ConverterRun_Click(object sender,RoutedEventArgs e)=>await BeginConversionAsync("convert");
    private async Task BeginConversionAsync(string command)
    {
        if(_converterStarting)return;
        try
        {
            var request=ReadConversionRequest();
            if(command=="convert"&&request.ValidateEngine is {Length:>0})
            {
                var dialog=new ContentDialog{Title="Run a GPU verification after conversion?",Content="This starts the selected engine and performs a real text inference test. It may use substantial VRAM. Nothing is loaded until conversion finishes.",PrimaryButtonText="Convert and test",CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot};
                if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
            }
            _converterStarting=true;UpdateConverterControls();
            ConverterInfo.IsOpen=false;ConverterPlanText.Text="";ConverterAddButton.Visibility=Visibility.Collapsed;
            await _backend.StartConversionAsync(command,request);
            _pendingConversionRequest=request;
            _checkedConversion=command=="check"?request:null;
            await PollConverterAsync();_converterTimer.Start();
        }
        catch(Exception ex){ConverterError(ex);}
        finally{_converterStarting=false;UpdateConverterControls();}
    }
    private async void ConverterCancel_Click(object sender,RoutedEventArgs e)
    {try{await _backend.CancelConversionAsync();await PollConverterAsync();}catch(Exception ex){ConverterError(ex);}}

    private async Task PollConverterAsync()
    {
        if(_pollingConverter||_hiddenToTray||_closing)return;
        _pollingConverter=true;
        try
        {
            _conversionStatus=await _backend.ConversionStatusAsync();
            if(_conversionStatus is not { } state)return;
            ConverterStatusText.Text=state.Cancelling||state.Phase==state.Message?state.Message:$"{state.Phase} · {state.Message}";
            if(state.Percent is { } percent){ConverterProgress.IsIndeterminate=false;ConverterProgress.Value=percent;ConverterStatusText.Text+=$" · {percent:0.0}%";}
            else ConverterProgress.IsIndeterminate=true;
            if(state.Plan is {Length:>0})ConverterPlanText.Text=DescribeConversionPlan(state.Plan);
            if(!state.Running)
            {
                _converterTimer.Stop();
                CompleteConversionWorkflow(state);
                if(state.JobId.Length>0&&_lastConversionNotice!=state.JobId)
                {
                    _lastConversionNotice=state.JobId;
                    if(state.Phase is "Failed" or "Cancelled")_checkedConversion=null;
                    ShowInfoBar(ConverterInfo,state.Phase,state.Message,state.Phase=="Failed"?InfoBarSeverity.Error:state.Phase=="Cancelled"?InfoBarSeverity.Informational:InfoBarSeverity.Success);
                }
                ConverterAddButton.Visibility=state.Phase=="Complete"&&state.Output is {Length:>0}?Visibility.Visible:Visibility.Collapsed;
            }
            else _converterTimer.Start();
            UpdateConverterControls();
        }
        catch(Exception ex){_converterTimer.Stop();ConverterError(ex);}
        finally{_pollingConverter=false;}
    }
    private static string DescribeConversionPlan(string raw)
    {
        using var document=JsonDocument.Parse(raw);var root=document.RootElement;var lines=new List<string>();
        foreach(var key in new[]{"mode","architecture","recipe"})if(root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String)lines.Add($"{char.ToUpperInvariant(key[0])+key[1..]}: {value.GetString()}");
        if(root.TryGetProperty("components",out var components)&&components.ValueKind==JsonValueKind.Array)lines.Add("Components: "+string.Join(", ",components.EnumerateArray().Select(x=>x.GetString())));
        if(root.TryGetProperty("encodedBytes",out var bytes)&&bytes.TryGetInt64(out var size))lines.Add($"Estimated encoded weights: {size/1073741824d:0.00} GiB (resources, staging and shards need additional disk space).");
        if(root.TryGetProperty("formats",out var formats)&&formats.ValueKind==JsonValueKind.Object)lines.Add("Output encoding: "+string.Join(", ",formats.EnumerateObject().Select(x=>x.Name)));
        lines.Add("Structural compatibility checked; GPU execution is not yet verified.");return string.Join("\n",lines);
    }
    private async void ConverterAdd_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            if(_conversionStatus?.Output is not {Length:>0} output)return;
            await _backend.LinkModelAsync(output);await RefreshModelsAsync();NavigateToPage("models");
            ShowInfoBar(ModelsInfo,"Converted model added","The output is linked without copying or loading it. Choose its runtime settings before use.",InfoBarSeverity.Success);
        }
        catch(Exception ex){ConverterError(ex);}
    }
    private void ConverterError(Exception ex)=>ShowInfoBar(ConverterInfo,"Conversion action failed",ex is OverflowException?"Enter valid whole-number chunk/proposal rows and a valid shard size.":ex.Message,InfoBarSeverity.Error);

    private async Task<bool> ConfirmConverterExitAsync()
    {
        var status=await _backend.ConversionStatusAsync();
        if(status?.Running!=true)return true;
        if(_hiddenToTray)RestoreFromTray();
        var dialog=new ContentDialog{Title="A conversion is still running",Content="Keep Manager in the tray to continue. Exiting cancels the converter and waits for its temporary output cleanup.",PrimaryButtonText="Cancel conversion and exit",CloseButtonText="Keep running",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot};
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return false;
        await _backend.CancelConversionAsync();
        var deadline=DateTimeOffset.UtcNow.AddSeconds(40);
        while(DateTimeOffset.UtcNow<deadline)
        {
            var current=await _backend.ConversionStatusAsync();
            if(current?.Running==false)return true;
            await Task.Delay(250);
        }
        ShowGlobal("Conversion is still stopping","Wait for cleanup, then try Exit again. Manager stays open to protect the conversion output.",InfoBarSeverity.Warning);
        return false;
    }
}
