using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NInferManager.Contracts;

namespace NInferManager.WinUI;

public sealed partial class MainWindow
{
    private bool _conversionWizard=true;
    private int _conversionStep;
    private JsonElement? _conversionDiscovery;
    private JsonElement? _conversionAnalysis;
    private JsonElement? _selectedConversionOption;
    private ConversionRequest? _pendingConversionRequest;
    private string? _handledConversionJob;
    private readonly Dictionary<string,TextBox> _converterResources=[];
    private readonly List<ConversionAssignmentEditor> _conversionAssignments=[];
    private readonly TextBox _converterConfigFile=new(){Header="Individual config.json override"};
    private readonly TextBox _converterResourceFolder=new(){Header="Separate tokenizer / processor folder"};
    private readonly JsonSerializerOptions _conversionJson=new(JsonSerializerDefaults.Web){WriteIndented=true};

    private void InitializeConversionEditors()
    {
        ConverterDevicePicker.Items.Add("cpu");ConverterDevicePicker.Items.Add("auto");ConverterDevicePicker.SelectedIndex=0;
        foreach(var box in new[]{_converterConfigFile,_converterResourceFolder})
        {box.TextChanged+=ConverterInput_Changed;ConverterResourcesPanel.Children.Add(ConversionPathEditor(box,ReferenceEquals(box,_converterConfigFile)?"file":"folder"));}
        foreach(var role in _converterCapabilities!.ResourceRoles)
        {
            var box=new TextBox{Header=HelpLabel(role,"Use this file from the exact source checkpoint. Leave blank to discover it in the matching folder.")};
            box.TextChanged+=ConverterInput_Changed;_converterResources.Add(role,box);
            ConverterResourcesPanel.Children.Add(ConversionPathEditor(box,"file"));
        }
    }

    private static Dictionary<string,string> ReadConversionMap(TextBox box,string label)
    {
        try{return JsonSerializer.Deserialize<Dictionary<string,string>>(string.IsNullOrWhiteSpace(box.Text)?"{}":box.Text)
            ??throw new JsonException("Expected an object.");}
        catch(JsonException){throw new InvalidOperationException($"{label}: enter a JSON object mapping names to file paths, for example {{\"source\":\"D:/Models/model.gguf\"}}.");}
    }
    private static int ReadConversionInteger(NumberBox box,string label)
    {
        if(!double.IsFinite(box.Value)||box.Value!=Math.Round(box.Value))throw new InvalidOperationException($"{label}: enter a whole number.");
        return checked((int)box.Value);
    }

    private FrameworkElement ConversionPathEditor(TextBox box,string kind)
    {
        var panel=new StackPanel{Spacing=6};panel.Children.Add(box);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};
        void Add(string text,bool folder)
        {
            var button=new Button{Content=text};
            button.Click+=async(_,_)=>
            {
                try
                {
                    if(folder)
                    {
                        var picker=new Windows.Storage.Pickers.FolderPicker();picker.FileTypeFilter.Add("*");
                        WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
                        if(await picker.PickSingleFolderAsync() is { } selected)box.Text=selected.Path;
                    }
                    else
                    {
                        var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add("*");
                        WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
                        if(await picker.PickSingleFileAsync() is { } selected)box.Text=selected.Path;
                    }
                }
                catch(Exception ex){ConverterError(ex);}
            };
            buttons.Children.Add(button);
        }
        if(kind!="folder")Add("Choose file",false);
        if(kind!="file")Add("Choose folder",true);
        panel.Children.Add(buttons);return panel;
    }

    private void InvalidateConversionWorkflow(object sender)
    {
        if(ReferenceEquals(sender,ConverterSourceBox))
        {_conversionDiscovery=null;_conversionAnalysis=null;_selectedConversionOption=null;_conversionStep=0;ConverterRequirementsPanel.Children.Clear();}
        else if(!new object[]{ConverterOutputBox,ConverterShardBox,ConverterNameBox,ConverterValidateBox,ConverterValidateTimeoutBox}.Contains(sender))
        {
            _conversionAnalysis=null;_selectedConversionOption=null;
            if(_conversionWizard&&_conversionStep>=2)_conversionStep=1;
        }
        ConverterPlanText.Text="Settings changed. Check the plan again before converting.";
        ConverterWeightList.ItemsSource=null;
    }

    private void ConverterMode_Click(object sender,RoutedEventArgs e)
    {
        if(_conversionStatus?.Running==true||_converterStarting)return;
        _conversionWizard=ReferenceEquals(sender,ConverterWizardMode);
        if(_conversionWizard)_conversionStep=_checkedConversion is not null?3:_conversionDiscovery is not null?1:0;
        UpdateConverterControls();
    }
    private void ConverterBack_Click(object sender,RoutedEventArgs e)
    {
        if(_conversionStep>0)_conversionStep--;
        if(_conversionStep==2&&_conversionAnalysis is null)_conversionStep=1;
        UpdateConverterControls();
    }
    private async void ConverterNext_Click(object sender,RoutedEventArgs e)
    {
        if(_converterStarting||_conversionStatus?.Running==true)return;
        switch(_conversionStep)
        {
            case 0:await BeginConversionAsync("probe");break;
            case 1:await BeginConversionAsync("analyze");break;
            case 2:
                if(_selectedConversionOption is null)return;
                _conversionStep=3;UpdateConverterControls();await BeginConversionAsync("check");break;
            case 3:await BeginConversionAsync(_checkedConversion is null?"check":"convert");break;
            default:
                _conversionStep=0;_checkedConversion=null;_conversionDiscovery=null;_conversionAnalysis=null;_selectedConversionOption=null;
                ConverterSourceBox.Text="";ConverterOutputBox.Text="";ConverterPlanText.Text="";ConverterWeightList.ItemsSource=null;
                ConverterAddButton.Visibility=Visibility.Collapsed;UpdateConverterControls();break;
        }
        ConverterPage.ChangeView(null,0,null,true);
    }

    private void UpdateConversionWorkflow(bool busy,bool artifact)
    {
        Visibility Show(bool value)=>value?Visibility.Visible:Visibility.Collapsed;
        ConverterWizardMode.IsChecked=_conversionWizard;ConverterManualMode.IsChecked=!_conversionWizard;
        ConverterWizardMode.IsEnabled=ConverterManualMode.IsEnabled=!busy;
        ConverterSourceCard.Visibility=Show(!_conversionWizard||_conversionStep<=1);
        ConverterResourcesCard.Visibility=Show(!artifact&&(!_conversionWizard||_conversionStep==1));
        ConverterOutputCard.Visibility=Show(!_conversionWizard||_conversionStep==3);
        ConverterAdvancedCard.Visibility=Show(!_conversionWizard||_conversionStep is 1 or 3);
        ConverterOptionsCard.Visibility=Show(_conversionWizard&&_conversionStep==2);
        ConverterProfilePicker.Visibility=Show(!_conversionWizard);
        ConverterStepText.Text=_conversionWizard?(_conversionStep switch
        {0=>"Step 1 of 4 · Choose your source",1=>"Step 2 of 4 · Check required files",2=>"Step 3 of 4 · Compare conversion options",3=>"Step 4 of 4 · Review & save",_=>"Conversion complete"}):"MANUAL SETUP · The same converter and settings, with full control";
        ConverterBackButton.Visibility=Show(_conversionWizard&&_conversionStep is >0 and <4);
        ConverterBackButton.IsEnabled=!busy;
        ConverterNextButton.Visibility=Show(_conversionWizard);
        ConverterNextButton.Content=_conversionStep switch{0=>"Check model",1=>"Scan conversion options",2=>"Review selection",3=>_checkedConversion is null?"Check final plan":"Convert model",_=>"Convert another model"};
        ConverterNextButton.IsEnabled=!busy&&_converterCapabilities is not null&&(_conversionStep==4||!string.IsNullOrWhiteSpace(ConverterSourceBox.Text))&&(_conversionStep!=2||_selectedConversionOption is not null);
        ConverterCheckButton.Visibility=ConverterRunButton.Visibility=Show(!_conversionWizard);
        ConverterDevicePicker.IsEnabled=ConverterExtraSourcesBox.IsEnabled=ConverterShardsBox.IsEnabled=ConverterAssignmentAddButton.IsEnabled=!busy&&!artifact;
        SetConversionEditorsEnabled(ConverterAssignmentsPanel,!busy&&!artifact);
        ConverterValidateTimeoutBox.IsEnabled=!busy;
        foreach(var entry in _converterResources)entry.Value.IsEnabled=!busy&&!artifact&&(!entry.Key.Contains("preprocessor")||_converterComponents.GetValueOrDefault("vision")?.IsChecked==true);
        if(_selectedConversionOption is { } selected&&_conversionStep==3)
        {
            ConverterProfileHint.Text="Selected: "+selected.GetProperty("profile").GetString()!.ToUpperInvariant();
            if(selected.TryGetProperty("warnings",out var warnings))ConverterProfileHint.Text+="\n"+string.Join("\n",warnings.EnumerateArray().Select(x=>"• "+x.GetString()));
        }
    }

    private static void SetConversionEditorsEnabled(DependencyObject parent,bool enabled)
    {
        if(parent is Control control)control.IsEnabled=enabled;
        for(var i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);i++)
            SetConversionEditorsEnabled(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent,i),enabled);
    }

    private void CompleteConversionWorkflow(ConversionStatus state)
    {
        if(state.JobId.Length==0||_handledConversionJob==state.JobId)return;
        _handledConversionJob=state.JobId;
        if(state.Phase is "Failed" or "Cancelled"){_checkedConversion=null;return;}
        if(state.Plan is {Length:>0} raw)
        {
            using var document=JsonDocument.Parse(raw);var result=document.RootElement.Clone();
            if(state.Command is "probe" or "analyze")
            {
                _conversionDiscovery=result;RenderConversionRequirements(result);
                if(_conversionWizard)_conversionStep=1;
                if(state.Command=="analyze")
                {
                    _conversionAnalysis=result;_selectedConversionOption=null;RenderConversionOptions(result);
                    if(_conversionWizard&&result.TryGetProperty("options",out var options)&&options.GetArrayLength()>0)_conversionStep=2;
                }
                ConverterPlanText.Text="";
            }
            else if(state.Command=="inspect")
            {
                if(_conversionWizard&&_selectedConversionOption is { } selected&&
                    (selected.GetProperty("plan").GetProperty("planSignature").GetString()!=result.GetProperty("planSignature").GetString()||
                    _conversionAnalysis is { } analysis&&analysis.TryGetProperty("fileState",out var scannedFiles)&&result.TryGetProperty("fileState",out var checkedFiles)&&!SameConversionFileState(scannedFiles,checkedFiles)))
                {
                    _checkedConversion=null;_selectedConversionOption=null;_conversionAnalysis=null;_conversionStep=1;_lastConversionNotice=state.JobId;UpdateConverterControls();
                    throw new InvalidOperationException("The source files or conversion plan changed after you selected an option. Scan the options again and review the updated plan before converting.");
                }
                _checkedConversion=_pendingConversionRequest;
                RenderConversionWeights();
            }
        }
        if(state.Phase=="Complete"&&_conversionWizard)_conversionStep=4;
    }
    private static bool SameConversionFileState(JsonElement first,JsonElement second)
    {
        var left=first.EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value);
        var right=second.EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value);
        return left.Count==right.Count&&left.All(x=>right.TryGetValue(x.Key,out var value)&&x.Value.GetProperty("bytes").GetInt64()==value.GetProperty("bytes").GetInt64()&&x.Value.GetProperty("mtimeNs").GetInt64()==value.GetProperty("mtimeNs").GetInt64());
    }

    private void RenderConversionRequirements(JsonElement discovery)
    {
        ConverterRequirementsPanel.Children.Clear();
        var summary=new List<string>();
        if(discovery.TryGetProperty("source",out var source))
        {
            foreach(var key in new[]{"container","architecture"})if(source.TryGetProperty(key,out var value))summary.Add(value.ToString());
            if(source.TryGetProperty("weightBytes",out var bytes))summary.Add($"{bytes.GetInt64()/1073741824d:0.00} GiB source weights");
        }
        if(discovery.TryGetProperty("message",out var message))summary.Add(message.GetString()??"");
        if(discovery.TryGetProperty("issues",out var issues))foreach(var issue in issues.EnumerateArray())summary.Add(issue.GetProperty("message").GetString()??"");
        ConverterDiscoveryText.Text=string.Join("\n",summary);
        if(!discovery.TryGetProperty("requirements",out var requirements))return;
        foreach(var item in requirements.EnumerateArray())
        {
            var key=item.GetProperty("key").GetString()!;var status=item.GetProperty("status").GetString()!;
            var panel=new StackPanel{Spacing=6};
            var box=new TextBox{Text=item.GetProperty("path").GetString()??"",Header=(status=="ready"?"✓ ":"! ")+item.GetProperty("label").GetString()};
            box.TextChanged+=(_,_)=>
            {
                if(key=="configFile")_converterConfigFile.Text=box.Text;
                else if(key.StartsWith("resource:")&&_converterResources.TryGetValue(key[9..],out var resource))resource.Text=box.Text;
                else if(key.StartsWith("source:"))
                {
                    var name=key[7..];
                    if(_converterSources.TryGetValue(name,out var companion))companion.Text=box.Text;
                    else SetConversionMapEntry(ConverterExtraSourcesBox,name,box.Text);
                }
                else if(key.StartsWith("shard:"))SetConversionMapEntry(ConverterShardsBox,key[6..],box.Text);
            };
            panel.Children.Add(ConversionPathEditor(box,item.GetProperty("kind").GetString()??"file"));
            panel.Children.Add(new TextBlock{Text=item.GetProperty("help").GetString(),TextWrapping=TextWrapping.Wrap,Style=(Style)Application.Current.Resources["CaptionStyle"]});
            ConverterRequirementsPanel.Children.Add(panel);
        }
    }
    private static void SetConversionMapEntry(TextBox box,string key,string value)
    {
        var map=ReadConversionMap(box,"File replacements");
        if(string.IsNullOrWhiteSpace(value))map.Remove(key);else map[key]=value;
        box.Text=JsonSerializer.Serialize(map,new JsonSerializerOptions{WriteIndented=true});
    }

    private void RenderConversionOptions(JsonElement analysis)
    {
        ConverterOptionsPanel.Children.Clear();
        if(!analysis.TryGetProperty("options",out var options))return;
        foreach(var item in options.EnumerateArray())
        {
            var option=item.Clone();var supported=item.GetProperty("supported").GetBoolean();var profile=item.GetProperty("profile").GetString()!;
            var panel=new StackPanel{Spacing=8};
            panel.Children.Add(new TextBlock{Text=profile.ToUpperInvariant(),Style=(Style)Application.Current.Resources["SectionTitleStyle"]});
            var text=supported?DescribeConversionPlan(item.GetProperty("plan").GetRawText()):"Unavailable: "+item.GetProperty("reason").GetString();
            if(item.TryGetProperty("warnings",out var warnings))text+="\n"+string.Join("\n",warnings.EnumerateArray().Select(x=>"• "+x.GetString()));
            panel.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Style=(Style)Application.Current.Resources["CaptionStyle"]});
            var button=new Button{Content="Select "+profile.ToUpperInvariant(),IsEnabled=supported};
            button.Click+=(_,_)=>
            {
                _settingConverterControls=true;
                try{ConverterProfilePicker.SelectedItem=ConverterProfilePicker.Items.OfType<ComboBoxItem>().First(x=>x.Tag?.ToString()==profile);}
                finally{_settingConverterControls=false;}
                _selectedConversionOption=option;_checkedConversion=null;
                ConverterPlanText.Text="Selected "+profile.ToUpperInvariant()+". Review the output location before converting.";
                foreach(var child in ConverterOptionsPanel.Children.OfType<Border>())
                    if(child.Child is StackPanel contents&&contents.Children.LastOrDefault() is Button other)other.Content=ReferenceEquals(other,button)?"Selected ✓":"Select "+other.Tag;
                UpdateConverterControls();
            };
            button.Tag=profile.ToUpperInvariant();panel.Children.Add(button);
            ConverterOptionsPanel.Children.Add(new Border{Style=(Style)Application.Current.Resources["CardStyle"],Child=panel});
        }
    }

    private void ConverterWeightFilter_Changed(object sender,TextChangedEventArgs e)
    {if(_converterReady)RenderConversionWeights();}
    private void RenderConversionWeights()
    {
        ConverterWeightList.ItemsSource=null;
        if(_conversionStatus?.Command!="inspect"||_conversionStatus.Plan is not {Length:>0} raw)return;
        using var document=JsonDocument.Parse(raw);
        if(!document.RootElement.TryGetProperty("tensorPlan",out var tensors))return;
        var query=ConverterWeightFilter.Text.Trim();
        ConverterWeightList.ItemsSource=tensors.EnumerateArray().Select(t=>string.Join(", ",t.GetProperty("parameters").EnumerateArray().Select(x=>x.GetString()))+"\n"+t.GetProperty("format").GetString()+" · "+t.GetProperty("method").GetString()+$" · {t.GetProperty("encodedBytes").GetInt64()/1048576d:0.00} MiB")
            .Where(x=>x.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void ConverterAssignmentAdd_Click(object sender,RoutedEventArgs e)=>AddConversionAssignment(new());
    private void AddConversionAssignment(ConversionTensorOverride value)
    {
        var editor=new ConversionAssignmentEditor(value,_converterCapabilities!);
        editor.Changed+=()=>ConverterInput_Changed(editor,new RoutedEventArgs());
        editor.Remove.Click+=(_,_)=>{_conversionAssignments.Remove(editor);ConverterAssignmentsPanel.Children.Remove(editor.View);ConverterInput_Changed(editor,new RoutedEventArgs());};
        _conversionAssignments.Add(editor);ConverterAssignmentsPanel.Children.Add(editor.View);
        if(!_settingConverterControls)ConverterInput_Changed(editor,new RoutedEventArgs());
    }

    private async void ConverterImport_Click(object sender,RoutedEventArgs e)
    {
        if(_conversionStatus?.Running==true||_converterStarting)return;
        try
        {
            await OpenConverterAsync();if(_converterCapabilities is null)return;
            var picker=new Windows.Storage.Pickers.FileOpenPicker();picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
            if(await picker.PickSingleFileAsync() is not { } file)return;
            var request=JsonSerializer.Deserialize<ConversionRequest>(await File.ReadAllTextAsync(file.Path),_conversionJson)??throw new InvalidOperationException("Choose a conversion request JSON object.");
            if(request.Components is null||request.Sources is null||request.Resources is null||request.ShardOverrides is null||request.TensorOverrides is null)throw new InvalidOperationException("The request has invalid lists or file maps.");
            ApplyConversionRequest(request);
            ShowInfoBar(ConverterInfo,"Request imported","No code was executed and no conversion started. Review the settings, trust custom recipes only if appropriate, then check the plan.",InfoBarSeverity.Success);
        }
        catch(Exception ex){ConverterError(ex);}
    }
    private async void ConverterExport_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var request=ReadConversionRequest() with{AllowCustomCode=false};
            var picker=new Windows.Storage.Pickers.FileSavePicker{SuggestedFileName="convert"};picker.FileTypeChoices.Add("Conversion request",new List<string>{".json"});
            WinRT.Interop.InitializeWithWindow.Initialize(picker,_windowHandle);
            if(await picker.PickSaveFileAsync() is { } file)await File.WriteAllTextAsync(file.Path,JsonSerializer.Serialize(request,_conversionJson));
        }
        catch(Exception ex){ConverterError(ex);}
    }
    private void ApplyConversionRequest(ConversionRequest request)
    {
        if(!_converterCapabilities!.Profiles.Contains(request.Profile)||request.Device is not ("cpu" or "auto"))throw new InvalidOperationException("This request uses an unsupported profile or conversion device. The bundled runtime supports CPU and Auto (CPU).");
        _settingConverterControls=true;
        try
        {
            ConverterSourceBox.Text=request.Source;ConverterOutputBox.Text=request.Output;ConverterConfigBox.Text=request.ConfigFolder??"";
            _converterConfigFile.Text=request.ConfigFile??"";_converterResourceFolder.Text=request.ResourceFolder??"";
            ConverterProfilePicker.SelectedItem=ConverterProfilePicker.Items.OfType<ComboBoxItem>().First(x=>x.Tag?.ToString()==request.Profile);
            foreach(var component in _converterComponents)component.Value.IsChecked=request.Components.Contains(component.Key);
            foreach(var source in _converterSources)source.Value.Text=request.Sources.GetValueOrDefault(source.Key,"");
            ConverterExtraSourcesBox.Text=JsonSerializer.Serialize(request.Sources.Where(x=>!_converterSources.ContainsKey(x.Key)).ToDictionary(),_conversionJson);
            foreach(var role in _converterResources)role.Value.Text=request.Resources.GetValueOrDefault(role.Key,"");
            ConverterTemplateBox.Text=request.Resources.GetValueOrDefault("chat_template.jinja","");
            ConverterShardsBox.Text=JsonSerializer.Serialize(request.ShardOverrides,_conversionJson);
            ConverterRecipePicker.SelectedItem=_converterCapabilities.Recipes.Contains(request.Recipe)?request.Recipe:null;
            ConverterCustomRecipeBox.Text=_converterCapabilities.Recipes.Contains(request.Recipe)?"":request.Recipe??"";
            ConverterOverrideBox.Text=request.Override??"";ConverterTrustCodeBox.IsChecked=false;
            ConverterDevicePicker.SelectedItem=request.Device;ConverterRowsBox.Value=request.RowsPerChunk;ConverterShardBox.Value=request.MaxFileBytes/1_000_000_000d;
            ConverterNameBox.Text=request.Name??"";ConverterProposalBox.IsChecked=request.Proposal;ConverterProposalRowsBox.Value=request.ProposalRows;ConverterRankingBox.Text=request.Ranking??"";
            ConverterValidateBox.Text=request.ValidateEngine??"";ConverterValidateTimeoutBox.Value=request.ValidateTimeoutSeconds;
            _conversionAssignments.Clear();ConverterAssignmentsPanel.Children.Clear();foreach(var row in request.TensorOverrides)AddConversionAssignment(row);
        }
        finally{_settingConverterControls=false;}
        _checkedConversion=null;_conversionAnalysis=null;_conversionDiscovery=null;_selectedConversionOption=null;_conversionStep=0;
        _conversionWizard=false;ConverterRequirementsPanel.Children.Clear();ConverterPlanText.Text="Imported settings. Check and review before converting.";UpdateConverterControls();
    }

    private sealed class ConversionAssignmentEditor
    {
        public event Action? Changed;
        public StackPanel View {get;}=new(){Spacing=8};
        public Button Remove {get;}=new(){Content="Remove assignment",HorizontalAlignment=HorizontalAlignment.Left};
        private readonly TextBox _selector=new(){Header="Parameter / wildcard"};
        private readonly TextBox _layout=new(){Header="Layout"};
        private readonly TextBox _parameters=new(){Header="Method parameters (JSON)",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};
        private readonly ComboBox _format=new(){Header="Encoding",HorizontalAlignment=HorizontalAlignment.Stretch};
        private readonly ComboBox _method=new(){Header="Conversion method",HorizontalAlignment=HorizontalAlignment.Stretch};
        private readonly ComboBox _activation=new(){Header="Activation policy",HorizontalAlignment=HorizontalAlignment.Stretch};
        public ConversionAssignmentEditor(ConversionTensorOverride value,ConversionCapabilities capabilities)
        {
            _selector.Text=value.Selector;_layout.Text=value.Layout;_parameters.Text=JsonSerializer.Serialize(value.Parameters);
            foreach(var id in capabilities.Formats)_format.Items.Add(id);
            foreach(var id in capabilities.Methods)_method.Items.Add(id);
            foreach(var id in new[]{"","A16Only","AllowA8","AllowA4"})_activation.Items.Add(id);
            if(!_format.Items.Contains(value.Format))_format.Items.Add(value.Format);
            if(!_method.Items.Contains(value.Method))_method.Items.Add(value.Method);
            _format.SelectedItem=value.Format;_method.SelectedItem=value.Method;_activation.SelectedItem=value.ActivationPolicy;
            foreach(var field in new FrameworkElement[]{_selector,_format,_method,_layout,_activation,_parameters,Remove})View.Children.Add(field);
            foreach(var box in new[]{_selector,_layout,_parameters})box.TextChanged+=(_,_)=>Changed?.Invoke();
            foreach(var box in new[]{_format,_method,_activation})box.SelectionChanged+=(_,_)=>Changed?.Invoke();
        }
        public ConversionTensorOverride Read()
        {
            try{return new(){Selector=_selector.Text.Trim(),Format=_format.SelectedItem?.ToString()??"",Method=_method.SelectedItem?.ToString()??"",Layout=_layout.Text.Trim(),ActivationPolicy=_activation.SelectedItem?.ToString()??"",Parameters=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(_parameters.Text)??throw new JsonException()};}
            catch(JsonException){throw new InvalidOperationException("Per-weight assignment parameters must be a JSON object, for example {}. Correct the assignment before checking.");}
        }
    }
}
