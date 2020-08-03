using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using AndcultureCode.ZoomClient;
using AndcultureCode.ZoomClient.Models;
using AndcultureCode.ZoomClient.Models.Users;

namespace czoom
{
    public static class Program
    {
        private static ZoomClient? _zoomClient;

        public static int Main(string[] args)
        {
            var app = new CommandLineApplication();

            app.HelpOption("-h|--help");
            var optionApiSecret = app.Option<string>("-s|--secret <SECRET>", " Your Zoom Api Secret",
                CommandOptionType.SingleValue, option => option.IsRequired(allowEmptyStrings: false));
            var optionApiKey = app.Option<string>("-k|--key <KEY>", " Your Zoom Api key",
                CommandOptionType.SingleValue, option => option.IsRequired(allowEmptyStrings: false));
            var optionDonorEmail = app.Option<string>("-d|--donor <DONOR>", "Specify Donor User Email",
                CommandOptionType.SingleValue);
            var optionRecipientEmail = app.Option<string>("-r|--recipient <DONOR>", "Specify Recipient User Email",
                CommandOptionType.SingleValue);
            Console.WriteLine(optionApiKey);
            Console.WriteLine(optionApiSecret);
            app.OnExecute(() =>
            {
                bool interactive = true;
                if (!optionApiSecret.HasValue() || !optionApiKey.HasValue())
                {
                    throw new ArgumentException("Must supply api key and secret, aborting...");
                }

                if (optionDonorEmail.HasValue() && optionRecipientEmail.HasValue())
                {
                    interactive = false;
                }

                _zoomClient = CreateZoomClient(optionApiSecret.ParsedValue, optionApiKey.ParsedValue);

                var allUsers = _zoomClient.Users.GetUsers().Users;
                var donorUser = SelectUser(allUsers, donor: true, interactive ? null : optionDonorEmail.ParsedValue);
                var recipientUser = SelectUser(allUsers, donor: false,
                    interactive ? null : optionRecipientEmail.ParsedValue);

                try
                {
                    SwapLicenses(donor: donorUser, recipient: recipientUser);
                    return 0;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return 1;
                }
            });

            return app.Execute(args);
        }

        private static UserTypes CheckLicenseStatus(User user)
        {
            try
            {
                var zUser = _zoomClient?.Users.GetUser(user.Id) ?? throw new NullReferenceException();
                return zUser.Type;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        private static void SwapLicenses(User donor, User recipient)
        {
            if (donor == null) throw new ArgumentNullException(nameof(donor));
            if (recipient == null) throw new ArgumentNullException(nameof(recipient));
            
            UpdateUser donorUpdateUser = new UpdateUser {Type = UserTypes.Basic};
            
            try
            {
                bool response = _zoomClient?.Users.UpdateUser(donor.Id, donorUpdateUser) ?? throw new NullReferenceException("_zoomClient no initialized");
                Console.WriteLine(response ? $"Removed License from donor {donor.Email}" : $"failed to remove license from donor {donor.Email}");
            }
            catch (Exception e)
            {
                Console.WriteLine("failed to remove license from donor");

                Console.WriteLine(e);
                throw;
            }

            Task.Delay(4000).Wait();
            try
            {
                var recipientUpdateUser = new UpdateUser {Type = UserTypes.Pro};
                bool response = _zoomClient.Users.UpdateUser(recipient.Id, recipientUpdateUser);
                Console.WriteLine(response ? $"Added license to recipient {recipient.Email}" : $"failed to add license to recipient {recipient.Email}");
                
            }
            catch (Exception e)
            {
                Console.WriteLine("failed to add license to recipient");

                Console.WriteLine(e);
                throw;
            }

            try
            {
                Console.WriteLine("Confirming license statuses, please wait");
                Task.Delay(3000).Wait();
                var rStatus = CheckLicenseStatus(recipient);
                
                var dStatus = CheckLicenseStatus(donor);
                Console.WriteLine($"{recipient.Email} has {rStatus.ToString()}");
                Console.WriteLine($"{donor.Email} has {dStatus.ToString()}");
                
                if (rStatus != UserTypes.Pro && dStatus != UserTypes.Basic)
                    throw new ApplicationException("Failure to swap licenses; \n");
                Console.WriteLine("------------FIN--------------");

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

        }

        private static User SelectUser(IList<User> userList, bool donor = false, string? tryParseEmail = null)
        {
            User selectedUser;

            if (userList.Count == 0)
                throw new ArgumentException("Value cannot be an empty collection.", nameof(userList));

            if (tryParseEmail != null)
            {
                selectedUser = userList.FirstOrDefault(x => x.Email == tryParseEmail);
                if (selectedUser != null)
                {
                    if (selectedUser.Type == UserTypes.Basic && donor)
                        throw new InvalidConstraintException(
                            $"{tryParseEmail} cannot be a donor as they do not currently have a license assigned to their account");
                    if (selectedUser.Type == UserTypes.Pro && !donor)
                        throw new InvalidConstraintException(
                            $"{tryParseEmail} cannot be a recipient as they already have a license assigned to their account");
                    return selectedUser;
                }

                throw new KeyNotFoundException($"unable to select user using provided email of {tryParseEmail}");
            }

            var usersMenuPromptStringBuilder = new StringBuilder();

            usersMenuPromptStringBuilder.Append(donor ? "Select Existing License Holder" : "Select license recipient");
            usersMenuPromptStringBuilder.AppendLine();

            foreach (var user in userList)
            {
                if (donor)
                {
                    if (user.Type != UserTypes.Pro && user.Type != UserTypes.Corporate) continue;
                    usersMenuPromptStringBuilder.Append($"{userList.IndexOf(user)}:  {user.Email}");
                    usersMenuPromptStringBuilder.AppendLine();
                }
                else
                {
                    if (user.Type != UserTypes.Basic) continue;
                    usersMenuPromptStringBuilder.Append($"{userList.IndexOf(user)}:  {user.Email}");
                    usersMenuPromptStringBuilder.AppendLine();
                }
            }

            int selectedUserIndex = Prompt.GetInt(usersMenuPromptStringBuilder.ToString());
            selectedUser = userList[selectedUserIndex];
            Console.WriteLine($"You've selected {selectedUser.Email} as {(donor ? "donor" : "recipient")} ");
            return selectedUser;
        }

        private static ZoomClient CreateZoomClient(string zoomApiSecret, string zoomApiKey)
        {
            var options = new ZoomClientOptions
            {
                ZoomApiKey = zoomApiKey,
                ZoomApiSecret = zoomApiSecret,
            };
            return new ZoomClient(options);
        }
    }
}