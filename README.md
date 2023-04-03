# AiPullRequestAssistor
A small program that when run directly in an azure devops environment sends all current PR changes to
an AI endpoint for code review and appends the comment to the PR.

## Usage
### Azure Devops Pipeline
Inside the pipline add a powershell taks to download and execute the assistor.

Prerequisites:
- Add a secret to the pipline called 'OpenAiApiKey' with the api key to the openAi API.
- The project collection build service for the pipeline needs the permission to comment on PRs.

On windows:
```yaml
- task: PowerShell@2
    displayName: Add AI PullRequest review comments
    env:
      SYSTEM_ACCESSTOKEN: $(System.AccessToken)
      OPENAIAPIKEY: $(OpenAiApiKey)
    inputs:
      targetType: 'inline'
      script: |
        $url = 'https://github.com/ChristophHornung/AiPullRequestAssistor/releases/download/v0.0.1/AiPullRequestAssistor-net7-winx64.zip'
        $extractPath = "$(Pipeline.Workspace)/AiPullRequestAssistor"
        $zipPath = "$extractPath/AiPullRequestAssistor.zip"
        New-Item -ItemType Directory -Force -Path $extractPath
        Invoke-WebRequest -Uri $url -OutFile $zipPath
        Expand-Archive -Path $zipPath -DestinationPath $extractPath
        $executablePath = Join-Path -Path $extractPath -ChildPath 'AiPullRequestAssistor.exe'
        $arguments = 'add-comment-devops', '-t', "${env:OPENAIAPIKEY}"
        & $executablePath $arguments
    condition: and(succeeded(), eq(variables['Build.Reason'], 'PullRequest'))
```

On linux:

```yaml
- task: PowerShell@2
    displayName: Add AI PullRequest review comments
    env:
      SYSTEM_ACCESSTOKEN: $(System.AccessToken)
      OPENAIAPIKEY: $(OpenAiApiKey)
    inputs:
      targetType: 'inline'
      script: |
        $url = 'https://github.com/ChristophHornung/AiPullRequestAssistor/releases/download/v0.0.1/AiPullRequestAssistor-net7-linuxx64.zip'
        $extractPath = "$(Pipeline.Workspace)/AiPullRequestAssistor"
        $zipPath = "$extractPath/AiPullRequestAssistor.zip"
        New-Item -ItemType Directory -Force -Path $extractPath
        Invoke-WebRequest -Uri $url -OutFile $zipPath
        Expand-Archive -Path $zipPath -DestinationPath $extractPath
        $executablePath = Join-Path -Path $extractPath -ChildPath 'AiPullRequestAssistor'
        $arguments = 'add-comment-devops', '-t', "${env:OPENAIAPIKEY}"
        & chmod +x $executablePath
        Start-Process -FilePath $executablePath -ArgumentList $arguments -NoNewWindow -Wait
    condition: and(succeeded(), eq(variables['Build.Reason'], 'PullRequest'))
```

To always use the latest release version (beware of breaking builds though) use:

```
https://github.com/ChristophHornung/AiPullRequestAssistor/releases/latest/download/AiPullRequestAssistor-net7-winx64.zip
```

or

```
https://github.com/ChristophHornung/AiPullRequestAssistor/releases/latest/download/AiPullRequestAssistor-net7-linuxx64.zip
```