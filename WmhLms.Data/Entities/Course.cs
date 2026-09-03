namespace WmhLms.Data.Entities;

public class Course
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "draft"; // draft | published
    public DateTime CreatedAt { get; set; }
    public List<Section> Sections { get; set; } = new();
}

public class Section
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public string Title { get; set; } = "";
    public int Order { get; set; }
    public List<Item> Items { get; set; } = new();
}

public class Item
{
    public long Id { get; set; }
    public long SectionId { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "video"; // video | text | quiz | audio
    public string ContentUrl { get; set; } = "";
    public string TextContent { get; set; } = "";
    public int Order { get; set; }
    public List<Question> Questions { get; set; } = new();
}

public class Question
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public string Type { get; set; } = "multiple_choice"; // multiple_choice | true_false | multiple_answer
    public string Prompt { get; set; } = "";
    public List<Option> Options { get; set; } = new();
}

public class Option
{
    public long Id { get; set; }
    public long QuestionId { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}
