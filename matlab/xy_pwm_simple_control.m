clc; clear; close all;

%% =========================================================
% HK_USB_DAQ V1.2 双轴 PWM 连续输出控制
%
% 只需要改下面参数：
% startTime_s  ：该轴从程序开始后第几秒启动
% dir          ：运动方向，+1 或 -1
% freq_Hz      ：PWM频率，决定速度
% duration_s   ：运动持续时间
%
% 默认共阳极接法：
% 5VO  -> PUL+
% PWMx -> PUL-
% 5VO  -> DIR+
% OUTx -> DIR-
%
% 共阳极逻辑：
% 运行：PWM占空比50%
% 停止：PWM占空比100%，PUL-保持高电平
%% =========================================================

%% ===== 1. DLL路径 =====
dllPath = 'D:\驱动电机\USBDAQ_DLL_V12X64.dll';
hPath   = 'D:\驱动电机\HKDAQ_MATLAB.h';
libName = 'HKDAQ';

%% =========================================================
% 2. 只改这里：双轴运动参数
%% =========================================================

% ---------- X轴参数 ----------
X_startTime_s = 10.0;      % X轴开始时间，单位 s
X_dir         = +1;       % X轴方向：+1 或 -1
X_freq_Hz     = 1500;      % X轴PWM频率，单位 Hz
X_duration_s  = 8.0;      % X轴运动时间，单位 s

% ---------- Y轴参数 ----------
Y_startTime_s = 0.0;      % Y轴开始时间，单位 s
Y_dir         = +1;       % Y轴方向：+1 或 -1
Y_freq_Hz     = 1000;      % Y轴PWM频率，单位 Hz
Y_duration_s  = 10.0;      % Y轴运动时间，单位 s

% 示例：
% 如果想 X、Y 同时开始：
% X_startTime_s = 0;
% Y_startTime_s = 0;
%
% 如果想 X 先动，Y 过 2 秒再动：
% X_startTime_s = 0;
% Y_startTime_s = 2;
%
% 如果想反向：
% X_dir = -1;
% Y_dir = -1;
%
% 如果想速度快一点：
% X_freq_Hz = 200;
% Y_freq_Hz = 200;

%% ===== 3. 端口定义 =====
% PWMOutSetV12 的 PWM 通道：
% PWM1 -> 通道1
% PWM2 -> 通道2

X.pwmChan = 1;     % PWM1 -> X轴 PUL-
Y.pwmChan = 2;     % PWM2 -> Y轴 PUL-

% DoSetV12 的数字输出通道：
% PWM1/OUT1 -> DO0
% PWM2/OUT2 -> DO1
% OUT3      -> DO2
% OUT4      -> DO3
% OUT5      -> DO4
% OUT6      -> DO5
% OUT7      -> DO6
% OUT8      -> DO7

X.dirDO = 2;       % OUT3 -> X轴 DIR-
Y.dirDO = 4;       % OUT5 -> Y轴 DIR-

% 如果方向反了，把对应轴改成 true
X.dirInvert = false;
Y.dirInvert = false;

%% ===== 4. PWM占空比设置 =====
runDuty  = 50;     % 运行，占空比50%
stopDuty = 100;    % 共阳极停止，占空比100%

%% ===== 5. 参数安全检查 =====
if X_freq_Hz < 1 || Y_freq_Hz < 1
    error('PWM频率不能小于1 Hz');
end

if X_duration_s < 0 || Y_duration_s < 0
    error('运动时间不能为负数');
end

if X_startTime_s < 0 || Y_startTime_s < 0
    error('开始时间不能为负数');
end

if ~ismember(X_dir, [-1, 1])
    error('X_dir 只能是 +1 或 -1');
end

if ~ismember(Y_dir, [-1, 1])
    error('Y_dir 只能是 +1 或 -1');
end

%% ===== 6. 加载DLL并打开采集卡 =====
if ~libisloaded(libName)
    loadlibrary(dllPath, hPath, 'alias', libName);
end

ret = calllib(libName, 'OpenUsbV12');
if ret ~= 0
    error('OpenUsbV12 打开失败，返回值 = %d', ret);
end

disp('采集卡打开成功');

cleanupObj = onCleanup(@() safeClose(libName, X, Y, stopDuty));

%% ===== 7. 初始化：停止两轴PWM =====
setPWM(libName, X.pwmChan, 100, stopDuty);
setPWM(libName, Y.pwmChan, 100, stopDuty);

pause(0.2);

%% ===== 8. 设置方向 =====
setAxisDir(libName, X, X_dir);
setAxisDir(libName, Y, Y_dir);

pause(0.1);

disp('方向设置完成，准备按时间表运动');

%% ===== 9. 生成运动事件表 =====
events = [];

% X轴启动事件
if X_duration_s > 0
    events(end+1).time = X_startTime_s;
    events(end).axis = 'X';
    events(end).action = 'start';

    events(end+1).time = X_startTime_s + X_duration_s;
    events(end).axis = 'X';
    events(end).action = 'stop';
end

% Y轴启动事件
if Y_duration_s > 0
    events(end+1).time = Y_startTime_s;
    events(end).axis = 'Y';
    events(end).action = 'start';

    events(end+1).time = Y_startTime_s + Y_duration_s;
    events(end).axis = 'Y';
    events(end).action = 'stop';
end

% 按时间排序
[~, idx] = sort([events.time]);
events = events(idx);

%% ===== 10. 执行运动 =====
t0 = tic;

for k = 1:length(events)

    targetTime = events(k).time;

    while toc(t0) < targetTime
        pause(0.001);
    end

    switch events(k).axis
        case 'X'
            if strcmp(events(k).action, 'start')
                fprintf('%.3f s：X轴启动，频率 %d Hz，方向 %+d\n', ...
                    toc(t0), X_freq_Hz, X_dir);
                setPWM(libName, X.pwmChan, X_freq_Hz, runDuty);
            else
                fprintf('%.3f s：X轴停止\n', toc(t0));
                setPWM(libName, X.pwmChan, X_freq_Hz, stopDuty);
            end

        case 'Y'
            if strcmp(events(k).action, 'start')
                fprintf('%.3f s：Y轴启动，频率 %d Hz，方向 %+d\n', ...
                    toc(t0), Y_freq_Hz, Y_dir);
                setPWM(libName, Y.pwmChan, Y_freq_Hz, runDuty);
            else
                fprintf('%.3f s：Y轴停止\n', toc(t0));
                setPWM(libName, Y.pwmChan, Y_freq_Hz, stopDuty);
            end
    end
end

%% ===== 11. 确保停止并关闭 =====
setPWM(libName, X.pwmChan, X_freq_Hz, stopDuty);
setPWM(libName, Y.pwmChan, Y_freq_Hz, stopDuty);

safeClose(libName, X, Y, stopDuty);

disp('双轴运动结束');

%% =========================================================
% 局部函数
%% =========================================================

function setAxisDir(libName, axisCfg, dirSign)
    % dirSign = +1 或 -1

    dirState = dirSign > 0;

    if axisCfg.dirInvert
        dirState = ~dirState;
    end

    writeDO(libName, axisCfg.dirDO, dirState);
end

function setPWM(libName, pwmChan, freq, duty)
    ret = calllib(libName, 'PWMOutSetV12', int32(pwmChan), int32(freq), single(duty));
    if ret ~= 0
        error('PWMOutSetV12 设置失败：PWM%d，freq=%d，duty=%.1f，返回值=%d', ...
            pwmChan, freq, duty, ret);
    end
end

function writeDO(libName, doChan, state)
    ret = calllib(libName, 'DoSetV12', uint8(doChan), uint8(state));
    if ret ~= 0
        error('DoSetV12 输出失败：DO%d，state=%d，返回值=%d', ...
            doChan, state, ret);
    end
end

function safeClose(libName, X, Y, stopDuty)
    try
        if libisloaded(libName)
            calllib(libName, 'PWMOutSetV12', int32(X.pwmChan), int32(100), single(stopDuty));
            calllib(libName, 'PWMOutSetV12', int32(Y.pwmChan), int32(100), single(stopDuty));
            calllib(libName, 'CloseUsbV12');
            unloadlibrary(libName);
        end
    catch
    end
end