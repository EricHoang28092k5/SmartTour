public class TourViewModel : BindableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public List<TourPoiDto> Pois { get; set; } = new();

    private bool isExpanded;
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            isExpanded = value;
            OnPropertyChanged();
        }
    }
}

public class TourResponse
{
    public bool Success { get; set; }
    public List<TourDto> Data { get; set; } = new();
}

public class TourDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<TourPoiDto>? Pois { get; set; }
}

public class TourPoiDto
{
    public int PoiId { get; set; }
    public string Name { get; set; } = "";
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int OrderIndex { get; set; }
}