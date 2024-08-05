{{- define "parcel-trace.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/* <release>-<service> */}}
{{- define "parcel-trace.fullname" -}}
{{- $root := index . 0 -}}
{{- $name := index . 1 -}}
{{- printf "%s-%s" $root.Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "parcel-trace.labels" -}}
{{- $root := index . 0 -}}
{{- $name := index . 1 -}}
helm.sh/chart: {{ printf "%s-%s" $root.Chart.Name $root.Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ $name }}
app.kubernetes.io/instance: {{ $root.Release.Name }}
app.kubernetes.io/part-of: {{ include "parcel-trace.name" $root }}
app.kubernetes.io/version: {{ $root.Values.global.imageTag | default $root.Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ $root.Release.Service }}
{{- end -}}

{{- define "parcel-trace.selectorLabels" -}}
{{- $root := index . 0 -}}
{{- $name := index . 1 -}}
app.kubernetes.io/name: {{ $name }}
app.kubernetes.io/instance: {{ $root.Release.Name }}
{{- end -}}

{{- define "parcel-trace.secretName" -}}
{{- if .Values.secrets.existingSecret -}}
{{- .Values.secrets.existingSecret -}}
{{- else -}}
{{- printf "%s-secrets" .Release.Name -}}
{{- end -}}
{{- end -}}

{{- define "parcel-trace.image" -}}
{{- $root := index . 0 -}}
{{- $svc := index . 1 -}}
{{- $registry := $root.Values.global.imageRegistry -}}
{{- $tag := $svc.image.tag | default $root.Values.global.imageTag | default $root.Chart.AppVersion -}}
{{- if $registry -}}
{{- printf "%s/%s:%s" $registry $svc.image.repository $tag -}}
{{- else -}}
{{- printf "%s:%s" $svc.image.repository $tag -}}
{{- end -}}
{{- end -}}

{{- define "parcel-trace.serviceAccountName" -}}
{{- printf "%s-app" .Release.Name -}}
{{- end -}}
