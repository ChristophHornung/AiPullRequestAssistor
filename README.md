# AiPullRequestAssistor
Adds AI generated code review comments to a PR running in Azure DevOps.

It's purpose is to be integrated into a Azure DevOps pipeline with as few configuration parts as possible.

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
        $url = 'https://github.com/ChristophHornung/AiPullRequestAssistor/releases/download/v0.0.2/AiPullRequestAssistor-net7-winx64.zip'
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
        $url = 'https://github.com/ChristophHornung/AiPullRequestAssistor/releases/download/v0.0.2/AiPullRequestAssistor-net7-linuxx64.zip'
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

### Additional options
```
Usage:
  AiPullRequestAssistor add-comment-devops [options]

Options:
  -t, --openAiToken <openAiToken> (REQUIRED)                  The access token for openAI.
  -m, --model                                                 The model to use for the AI request. [default:Gpt_3_5_Turbo]
  <Gpt_3_5_Turbo|Gpt_3_5_Turbo_16k|Gpt_4|Gpt_4_32k>
  --maxTotalToken <maxTotalToken>                             The maximum total token count to use for one PR comment.
                                                              Once the limit is reached no additional requests are made
                                                              for one PR - this means the total token count can be
                                                              slightly larger than this value. If the limit is reached by 
                                                              just counting the input no request will be made. Set to 0 to allow
                                                              arbitrarily large requests - BEWARE though this might
                                                              incur a very high cost for very large PRs. [default:
                                                              100000]
  --initialPrompt <initialPrompt>                             The initial prompt to start the AI conversation with, the
                                                              initial prompt is followed by all file changes in unidiff
                                                              format. Uses a default prompt otherwise.
  --azure <azure>                                             Uses the azure endpoints instead of the OpenAI api.
                                                              Requires a connection string in the form of
                                                              <deploymentId>.<resourceName>
```