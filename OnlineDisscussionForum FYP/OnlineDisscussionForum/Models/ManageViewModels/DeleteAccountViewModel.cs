using System.ComponentModel.DataAnnotations;
namespace OnlineDisscussionForum.Models.ManageViewModels;
public class DeleteAccountViewModel
{
    [Required, DataType(DataType.Password)] public string Password { get; set; }
    public bool Confirm { get; set; }
}
