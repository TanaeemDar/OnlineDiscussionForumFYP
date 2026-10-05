using System.ComponentModel.DataAnnotations;
namespace OnlineDisscussionForum.Models.Forum;
public class EditForumModel : AddForumModel
{
    public int Id { get; set; }
    [Required] public string Version { get; set; }
}
