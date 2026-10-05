using System.ComponentModel.DataAnnotations;
namespace OnlineDisscussionForum.Models;
public class ContentEditModel
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Title { get; set; }
    [Required, StringLength(20000)] public string Content { get; set; }
    [Required] public string Version { get; set; }
}
public class ReplyEditModel
{
    public int Id { get; set; }
    [Required, StringLength(20000)] public string Content { get; set; }
    [Required] public string Version { get; set; }
}
public class DeleteContentModel
{
    public int Id { get; set; }
    public string Title { get; set; }
    [Required] public string Version { get; set; }
}
