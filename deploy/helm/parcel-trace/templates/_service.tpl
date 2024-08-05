{{/* one service: configmap, deployment, service, hpa. args: (list $root $name $svc) */}}
{{- define "parcel-trace.service" -}}
{{- $root := index . 0 -}}
{{- $name := index . 1 -}}
{{- $svc := index . 2 -}}
{{- $fullname := include "parcel-trace.fullname" (list $root $name) -}}
apiVersion: v1
kind: ConfigMap
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "parcel-trace.labels" (list $root $name) | nindent 4 }}
data:
  OTEL_SERVICE_NAME: {{ $name | quote }}
  {{- range $key, $value := $root.Values.global.env }}
  {{ $key }}: {{ tpl (toString $value) $root | quote }}
  {{- end }}
  {{- range $key, $value := $svc.env }}
  {{ $key }}: {{ tpl (toString $value) $root | quote }}
  {{- end }}
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "parcel-trace.labels" (list $root $name) | nindent 4 }}
spec:
  {{- if not (and $svc.autoscaling $svc.autoscaling.enabled) }}
  replicas: {{ $svc.replicas | default 1 }}
  {{- end }}
  revisionHistoryLimit: 5
  selector:
    matchLabels:
      {{- include "parcel-trace.selectorLabels" (list $root $name) | nindent 6 }}
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 0
      maxSurge: 1
  template:
    metadata:
      labels:
        {{- include "parcel-trace.selectorLabels" (list $root $name) | nindent 8 }}
        {{- if $root.Values.workloadIdentity.enabled }}
        azure.workload.identity/use: "true"
        {{- end }}
      annotations:
        checksum/config: {{ toJson $svc.env | sha256sum | trunc 16 }}
    spec:
      serviceAccountName: {{ include "parcel-trace.serviceAccountName" $root }}
      {{- with $root.Values.global.imagePullSecrets }}
      imagePullSecrets:
        {{- toYaml . | nindent 8 }}
      {{- end }}
      securityContext:
        runAsNonRoot: true
        seccompProfile:
          type: RuntimeDefault
      containers:
        - name: {{ $name }}
          image: {{ include "parcel-trace.image" (list $root $svc) }}
          imagePullPolicy: {{ $root.Values.global.imagePullPolicy }}
          ports:
            - name: http
              containerPort: {{ $svc.port | default 8080 }}
              protocol: TCP
            {{- range $svc.extraPorts }}
            - name: {{ .name }}
              containerPort: {{ .port }}
              protocol: TCP
            {{- end }}
          envFrom:
            - configMapRef:
                name: {{ $fullname }}
          {{- with $svc.secretEnv }}
          env:
            {{- range $envName, $secretKey := . }}
            - name: {{ $envName }}
              valueFrom:
                secretKeyRef:
                  name: {{ include "parcel-trace.secretName" $root }}
                  key: {{ $secretKey }}
            {{- end }}
          {{- end }}
          livenessProbe:
            httpGet:
              path: /health
              port: http
            initialDelaySeconds: 10
            periodSeconds: 15
            timeoutSeconds: 3
            failureThreshold: 4
          readinessProbe:
            httpGet:
              path: /health/ready
              port: http
            initialDelaySeconds: 5
            periodSeconds: 10
            timeoutSeconds: 5
            failureThreshold: 3
          startupProbe:
            httpGet:
              path: /health
              port: http
            periodSeconds: 5
            failureThreshold: 30
          resources:
            {{- toYaml ($svc.resources | default $root.Values.global.resources) | nindent 12 }}
          securityContext:
            allowPrivilegeEscalation: false
            capabilities:
              drop: ["ALL"]
---
apiVersion: v1
kind: Service
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "parcel-trace.labels" (list $root $name) | nindent 4 }}
spec:
  type: ClusterIP
  selector:
    {{- include "parcel-trace.selectorLabels" (list $root $name) | nindent 4 }}
  ports:
    - name: http
      port: 80
      targetPort: http
      protocol: TCP
    {{- range $svc.extraPorts }}
    - name: {{ .name }}
      port: {{ .port }}
      targetPort: {{ .name }}
      protocol: TCP
      {{- if .appProtocol }}
      appProtocol: {{ .appProtocol }}
      {{- end }}
    {{- end }}
{{- if and $svc.autoscaling $svc.autoscaling.enabled }}
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: {{ $fullname }}
  labels:
    {{- include "parcel-trace.labels" (list $root $name) | nindent 4 }}
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: {{ $fullname }}
  minReplicas: {{ $svc.autoscaling.minReplicas | default 2 }}
  maxReplicas: {{ $svc.autoscaling.maxReplicas | default 6 }}
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: {{ $svc.autoscaling.targetCPUUtilizationPercentage | default 70 }}
    {{- with $svc.autoscaling.targetMemoryUtilizationPercentage }}
    - type: Resource
      resource:
        name: memory
        target:
          type: Utilization
          averageUtilization: {{ . }}
    {{- end }}
  behavior:
    scaleDown:
      stabilizationWindowSeconds: 300
{{- end }}
{{- end -}}
