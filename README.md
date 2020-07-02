# Bitbucket Issues to GitHub Script
A [LINQPad][linqpad] script to migrate issues from Bitbucket to a GitHub repository.

## Features

- Imports the issues, comments, and actions from the original bitbucket issues
- Closes issues that were marked closed it bitbucket (or marked duplicate/invalid/etc.) - configurable
- Allows replacing users with the corresponding github users
  - Note: all actions will be added to github as being created the github user running the script - however text/comments are added to denote who originally created the issue/comment.

What does the end result look like?  Take a look at [NUnit.ApplicationDomain early issues](https://github.com/zastrowm/NUnit.ApplicationDomain/issues?q=is%3Aissue+is%3Aclosed+sort%3Acreated-asc), specifically as an example, see [issue #1](https://github.com/zastrowm/NUnit.ApplicationDomain/issues/1)

## Missing Features

- Does not import attachments or images; PRs welcome

# Steps

Here's a warning:

> ### Warning
>
> It's highly suggested that you test importing into a scratch/private repository before importing into a live repository; this will allow you to verify it works and adjust as needed before performing the final import

1. Export the issues from Bitbucket repository into a json file
   1. Navigate to the repository settings
   2. Navigate to `Import & export` under the `Issues` section in the sidebar
   3. Click `Start Export`
   4. When it's completed, download the zip, and extract the files to a known location
2. If you don't already have [LINQPad][linqpad] installed, go install it; it's free and a great tool for .NET developers
3. Download the [`ImportFromBitbucket.linq`](./ImportFromBitbucket.linq) script in this repository
4. Open the script in linqpad
5. Configure the script as needed (see below)
6. Run the script using `F5` or the green play button

# Configuration

## Basic Options

Most of the configuration is straightforward - you'll need:

- the path to the exported bitbucket json (in 2.0 format)
- the name of the owner of the github repository
- the name of the github repository
- the url of the original bitbucket repository - to enable links back to the original

Additionally, you'll need a *personal access token*; this isn't specified in the script - it's retrieved via `Util.GetPassword` - but LINQPad will prompt you for it.  You can create a token via the [Personal access tokens](https://github.com/settings/tokens) page on github; feel free to delete the token after you're done importing. 

> ### Note: "Machine Account" user for issues
>
> Since all issues are created from the account of the personal-access-token, you may wish to create a secondary ["machine account"](https://docs.github.com/en/github/site-policy/github-terms-of-service#3-account-requirements) for the purposes of importing issues; per the link provided, this is totally okay by GitHub, and IMHO makes it much more clear that the issue was imported rather than native. 

## Closed Status

GitHub only has open or closed issues, while bitbucket has much more rich statuses like "duplicate", "invalid" etc.  You can specify the statuses that indicate that an issue is "closed".

## Mapping Users

The original author of an issue/comment/action is listed in each comment.  By default the original author's display name is set as the author; for example if I had commented on an issue, it would show up as "Mackenzie Zastrow commented".  If you instead want to map "Mackenzie Zastrow" to @zastrowm, you can use that using the `userMapping` dictionary.

[linqpad]: https://www.linqpad.net/

