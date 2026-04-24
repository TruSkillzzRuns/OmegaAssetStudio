using System;
using System.Globalization;
using System.Numerics;

namespace OmegaAssetStudio.WinUI.Modules.MaterialEditor.Core;

public sealed class MaterialParameter : NotifyPropertyChangedBase
{
    private string name = string.Empty;
    private string category = string.Empty;
    private float? scalarValue;
    private Vector4? vectorValue;
    private float? defaultScalarValue;
    private Vector4? defaultVectorValue;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string Category
    {
        get => category;
        set => SetProperty(ref category, value);
    }

    public float? ScalarValue
    {
        get => scalarValue;
        set
        {
            if (SetProperty(ref scalarValue, value))
                OnPropertyChanged(nameof(ScalarValueText));
        }
    }

    public Vector4? VectorValue
    {
        get => vectorValue;
        set => SetProperty(ref vectorValue, value);
    }

    public float? DefaultScalarValue
    {
        get => defaultScalarValue;
        set => SetProperty(ref defaultScalarValue, value);
    }

    public Vector4? DefaultVectorValue
    {
        get => defaultVectorValue;
        set => SetProperty(ref defaultVectorValue, value);
    }

    public string ScalarValueText
    {
        get => scalarValue.HasValue ? scalarValue.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ScalarValue = null;
                return;
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ||
                float.TryParse(value, out parsed))
            {
                ScalarValue = parsed;
            }
        }
    }

    public MaterialParameter Clone()
    {
        return new MaterialParameter
        {
            Name = Name,
            Category = Category,
            ScalarValue = ScalarValue,
            VectorValue = VectorValue,
            DefaultScalarValue = DefaultScalarValue,
            DefaultVectorValue = DefaultVectorValue
        };
    }

    public void CopyFrom(MaterialParameter source)
    {
        Name = source.Name;
        Category = source.Category;
        ScalarValue = source.ScalarValue;
        VectorValue = source.VectorValue;
        DefaultScalarValue = source.DefaultScalarValue;
        DefaultVectorValue = source.DefaultVectorValue;
    }
}

