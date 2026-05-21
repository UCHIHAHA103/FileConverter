// <copyright file="Settings.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

namespace FileConverter
{
    using System.Linq;
    using System.Xml.Serialization;
    using System.Collections.ObjectModel;
    using System.Globalization;

    using CommunityToolkit.Mvvm.ComponentModel;

    [XmlRoot]
    [XmlType]
    public class Settings : ObservableObject, IXmlSerializable
    {
        public const int Version = 4;

        private bool exitApplicationWhenConversionsFinished = false;
        private float durationBetweenEndOfConversionsAndApplicationExit = 3f;
        private ObservableCollection<ConversionPreset> conversionPresets = new ObservableCollection<ConversionPreset>();
        private bool checkUpgradeAtStartup = true;
        private CultureInfo applicationLanguage;
        private int maximumNumberOfSimultaneousConversions;
        private bool copyFilesInClipboardAfterConversion = false;
        private Helpers.HardwareAccelerationMode hardwareAccelerationMode = Helpers.HardwareAccelerationMode.Off;
        private bool autoRetrySoftwareEncodingOnGpuFailure = true;
        private bool minimizeToTray = false;
        private bool notifyOnComplete = true;
        private bool playSoundOnComplete = true;

        public ConversionPreset GetPresetFromName(string presetName)
        {
            return this.conversionPresets.FirstOrDefault(match => match.FullName == presetName);
        }

        public void Clean()
        {
            for (int index = 0; index < this.ConversionPresets.Count; index++)
            {
                this.ConversionPresets[index].Clean();
            }
        }
        
        public Settings Merge(Settings settings)
        {
            if (settings == null || settings.conversionPresets == null)
            {
                return this;
            }
            
            for (int index = 0; index < settings.conversionPresets.Count; index++)
            {
                ConversionPreset conversionPreset = settings.conversionPresets[index];
                if (this.conversionPresets.Any(match => match.FullName == conversionPreset.FullName))
                {
                    continue;
                }

                this.conversionPresets.Add(conversionPreset);
            }

            return this;
        }

        [XmlAttribute]
        public int SerializationVersion
        {
            get;
            set;
        } = Version;

        [XmlIgnore]
        public CultureInfo ApplicationLanguage
        {
            get
            {
                return this.applicationLanguage;
            }

            set
            {
                if (this.applicationLanguage != null && this.applicationLanguage.Equals(value))
                {
                    return;
                }

                this.applicationLanguage = value;
                if (this.applicationLanguage != null)
                {
                    // Applying the culture can throw on corner cases (e.g. invalid culture
                    // or satellite assembly missing). We never want this to bubble up and
                    // cause the Settings window to silently close without saving. See #750.
                    try
                    {
                        System.Threading.Thread.CurrentThread.CurrentCulture = this.applicationLanguage;
                        System.Threading.Thread.CurrentThread.CurrentUICulture = this.applicationLanguage;

                        // Also set Resources.Culture so that {x:Static project:Resources.XXX}
                        // bindings in XAML pick up the correct localized strings when windows
                        // are created. Without this, ResourceManager falls back to the neutral
                        // culture (English) even though CurrentUICulture is set correctly.
                        Properties.Resources.Culture = this.applicationLanguage;
                    }
                    catch (System.Exception exception)
                    {
                        Diagnostics.Debug.LogError($"Failed to apply application language '{this.applicationLanguage.Name}': {exception.Message}");
                    }
                }

                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public string ApplicationLanguageName
        {
            get
            {
                if (this.ApplicationLanguage == null)
                {
                    return string.Empty;
                }

                return this.ApplicationLanguage.Name;
            }

            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    this.ApplicationLanguage = null;
                    return;
                }

                this.ApplicationLanguage = CultureInfo.GetCultureInfo(value);
            }
        }

        [XmlIgnore]
        public ObservableCollection<ConversionPreset> ConversionPresets
        {
            get
            {
                return this.conversionPresets;
            }

            set
            {
                this.conversionPresets = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public bool ExitApplicationWhenConversionsFinished
        {
            get
            {
                return this.exitApplicationWhenConversionsFinished;
            }

            set
            {
                this.exitApplicationWhenConversionsFinished = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public float DurationBetweenEndOfConversionsAndApplicationExit
        {
            get
            {
                return this.durationBetweenEndOfConversionsAndApplicationExit;
            }

            set
            {
                this.durationBetweenEndOfConversionsAndApplicationExit = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public int MaximumNumberOfSimultaneousConversions
        {
            get
            {
                return this.maximumNumberOfSimultaneousConversions;
            }

            set
            {
                this.maximumNumberOfSimultaneousConversions = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement("ConversionPreset")]
        public ConversionPreset[] SerializableConversionPresets
        {
            get
            {
                return this.ConversionPresets.ToArray();
            }

            set
            {
                for (int index = 0; index < value.Length; index++)
                {
                    this.ConversionPresets.Add(value[index]);
                }
            }
        }

        [XmlElement]
        public bool CheckUpgradeAtStartup
        {
            get
            {
                return this.checkUpgradeAtStartup;
            }

            set
            {
                this.checkUpgradeAtStartup = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public bool CopyFilesInClipboardAfterConversion
        {
            get
            {
                return this.copyFilesInClipboardAfterConversion;
            }

            set
            {
                this.copyFilesInClipboardAfterConversion = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public Helpers.HardwareAccelerationMode HardwareAccelerationMode
        {
            get
            {
                return this.hardwareAccelerationMode;
            }

            set
            {
                this.hardwareAccelerationMode = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public bool AutoRetrySoftwareEncodingOnGpuFailure
        {
            get
            {
                return this.autoRetrySoftwareEncodingOnGpuFailure;
            }

            set
            {
                this.autoRetrySoftwareEncodingOnGpuFailure = value;
                this.OnPropertyChanged();
            }
        }

        [XmlElement]
        public bool MinimizeToTray
        {
            get => this.minimizeToTray;
            set { this.minimizeToTray = value; this.OnPropertyChanged(); }
        }

        [XmlElement]
        public bool NotifyOnComplete
        {
            get => this.notifyOnComplete;
            set { this.notifyOnComplete = value; this.OnPropertyChanged(); }
        }

        [XmlElement]
        public bool PlaySoundOnComplete
        {
            get => this.playSoundOnComplete;
            set { this.playSoundOnComplete = value; this.OnPropertyChanged(); }
        }

        public void OnDeserializationComplete()
        {
            this.DurationBetweenEndOfConversionsAndApplicationExit = System.Math.Max(0, System.Math.Min(10, this.DurationBetweenEndOfConversionsAndApplicationExit));

            for (int index = 0; index < this.ConversionPresets.Count; index++)
            {
                this.ConversionPresets[index].OnDeserializationComplete();
            }

            // Initialize application if it was not deserialized from the settings.
            if (this.ApplicationLanguage == null)
            {
                CultureInfo bestCandidate = null;
                CultureInfo currentUICulture = System.Threading.Thread.CurrentThread.CurrentUICulture;

                // Preferred default: Simplified Chinese (zh-CN).
                CultureInfo preferredDefault = CultureInfo.GetCultureInfo("zh-CN");

                foreach (CultureInfo culture in Helpers.GetSupportedCultures())
                {
                    if (culture.Name.Equals(preferredDefault.Name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        bestCandidate = culture;
                        break;
                    }
                    else if (culture.Equals(currentUICulture))
                    {
                        bestCandidate = culture;
                    }
                    else if (bestCandidate == null && culture.Equals(currentUICulture.Parent))
                    {
                        bestCandidate = culture;
                    }
                }

                if (bestCandidate != null)
                {
                    this.ApplicationLanguage = bestCandidate;
                }
                else
                {
                    Diagnostics.Debug.Log($"Can't find supported culture info for culture {currentUICulture}. Fallback to default culture.");
                    this.ApplicationLanguage = CultureInfo.GetCultureInfo("en");
                }
            }
        }
    }
}
