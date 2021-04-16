// Copyright (c) 2012-2021 Wojciech Figat. All rights reserved.

#include "Localization.h"
#include "CultureInfo.h"
#include "LocalizationSettings.h"
#include "Engine/Core/Log.h"
#include "Engine/Core/Config/GameSettings.h"
#include "Engine/Engine/EngineService.h"
#include "Engine/Content/Content.h"
#include "Engine/Profiler/ProfilerCPU.h"
#include "Engine/Serialization/Serialization.h"

class LocalizationService : public EngineService
{
public:
    CultureInfo CurrentCulture;
    CultureInfo CurrentLanguage;
    Array<AssetReference<LocalizedStringTable>> LocalizedStringTables;

    LocalizationService()
        : EngineService(TEXT("Localization"), -500)
        , CurrentCulture(0)
        , CurrentLanguage(0)
    {
    }

    void OnLocalizationChanged();

    bool Init() override;
};

namespace
{
    LocalizationService Instance;
}

IMPLEMENT_SETTINGS_GETTER(LocalizationSettings, Localization);

void LocalizationSettings::Apply()
{
    Instance.OnLocalizationChanged();
}

void LocalizationSettings::Deserialize(DeserializeStream& stream, ISerializeModifier* modifier)
{
    DESERIALIZE(LocalizedStringTables);
}

void LocalizationService::OnLocalizationChanged()
{
    PROFILE_CPU();

    Instance.LocalizedStringTables.Clear();

    // Collect all localization tables into mapping locale -> tables
    auto& settings = *LocalizationSettings::Get();
    Dictionary<String, Array<AssetReference<LocalizedStringTable>, InlinedAllocation<8>>> tables;
    for (auto& e : settings.LocalizedStringTables)
    {
        auto table = e.Get();
        if (table && !table->WaitForLoaded())
        {
            tables[table->Locale].Add(table);
        }
    }

    // Pick localization tables for a current language
    auto* table = tables.TryGet(Instance.CurrentLanguage.GetName());
    if (!table)
    {
        // Try using parent culture (eg. en if en-GB is missing)
        const CultureInfo parentLanguage(Instance.CurrentLanguage.GetParentLCID());
        if (parentLanguage.GetName().HasChars())
            table = tables.TryGet(parentLanguage.GetName());
        if (!table)
        {
            // Fallback to English
            const CultureInfo english("en");
            table = tables.TryGet(english.GetName());
        }
    }

    // Apply localization table
    if (table)
    {
        String locale;
        for (auto& e : tables)
        {
            if (&e.Value == table)
            {
                locale = e.Key;
                break;
            }
        }
        LOG(Info, "Using localization for {0}", locale);
        Instance.LocalizedStringTables.Add(table->Get(), table->Count());
    }

    // Send event
    Localization::LocalizationChanged();
}

bool LocalizationService::Init()
{
    // Use system language as default
    CurrentLanguage = CurrentCulture = CultureInfo(Platform::GetUserLocaleName());

    // Setup localization
    Instance.OnLocalizationChanged();

    return false;
}

Delegate<> Localization::LocalizationChanged;

const CultureInfo& Localization::GetCurrentCulture()
{
    return Instance.CurrentCulture;
}

void Localization::SetCurrentCulture(const CultureInfo& value)
{
    if (Instance.CurrentCulture == value)
        return;

    LOG(Info, "Changing current culture to: {0} ({1})", value.GetName(), value.GetLCID());
    Instance.CurrentCulture = value;
    Instance.OnLocalizationChanged();
}

const CultureInfo& Localization::GetCurrentLanguage()
{
    return Instance.CurrentLanguage;
}

void Localization::SetCurrentLanguage(const CultureInfo& value)
{
    if (Instance.CurrentLanguage == value)
        return;

    LOG(Info, "Changing current language to: {0} ({1})", value.GetName(), value.GetLCID());
    Instance.CurrentLanguage = value;
    Instance.OnLocalizationChanged();
}

void Localization::SetCurrentLanguageCulture(const CultureInfo& value)
{
    if (Instance.CurrentCulture == value && Instance.CurrentLanguage == value)
        return;

    LOG(Info, "Changing current language and culture to: {0} ({1})", value.GetName(), value.GetLCID());
    Instance.CurrentCulture = value;
    Instance.CurrentLanguage = value;
    Instance.OnLocalizationChanged();
}